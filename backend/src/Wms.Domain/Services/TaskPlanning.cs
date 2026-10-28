using Wms.Domain.Common;
using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Services;

/// <summary>Алгоритм порядка обхода: optimized (ближайший сосед + 2-opt) или baseline (по адресам ячеек).</summary>
public static class RouteAlgorithms
{
    public const string Optimized = "optimized";
    public const string Baseline = "baseline";

    /// <summary>Всё, кроме baseline, — optimized (как ветвление в моках).</summary>
    public static string Parse(string? value) => value == Baseline ? Baseline : Optimized;
}

/// <summary>Точка маршрута — ячейка с координатами на плане склада.</summary>
public sealed record RouteNode(string Code, double X, double Y);

/// <summary>Геометрия маршрута по манхэттенской метрике от входа (0,0) и обратно.</summary>
public static class RoutePlanner
{
    public static double Distance(double ax, double ay, double bx, double by) => Math.Abs(ax - bx) + Math.Abs(ay - by);

    /// <summary>Длина замкнутого маршрута вход → точки → вход, округлена до 0,1 м.</summary>
    public static double Length(IEnumerable<RouteNode> points)
    {
        double x = 0, y = 0, sum = 0;
        foreach (var p in points)
        {
            sum += Distance(x, y, p.X, p.Y);
            (x, y) = (p.X, p.Y);
        }
        return JsMath.Round1(sum + Distance(x, y, 0, 0));
    }

    /// <summary>Как в бумажном листе отбора — по коду ячейки (ordinal).</summary>
    public static IReadOnlyList<T> Baseline<T>(IEnumerable<T> items, Func<T, RouteNode> node) =>
        items.Select((item, index) => (item, index)).OrderBy(x => node(x.item).Code, StringComparer.Ordinal).ThenBy(x => x.index)
            .Select(x => x.item).ToList();

    /// <summary>Ближайший сосед от входа, затем 2-opt, пока маршрут сокращается больше чем на 1 см.</summary>
    public static IReadOnlyList<T> Optimized<T>(IEnumerable<T> items, Func<T, RouteNode> node)
    {
        var rest = items.ToList();
        var route = new List<T>(rest.Count);
        double x = 0, y = 0;
        while (rest.Count > 0)
        {
            var best = 0;
            for (var i = 0; i < rest.Count; i++)
            {
                var c = node(rest[i]);
                var b = node(rest[best]);
                if (Distance(x, y, c.X, c.Y) < Distance(x, y, b.X, b.Y)) best = i;
            }
            var next = rest[best];
            rest.RemoveAt(best);
            route.Add(next);
            (x, y) = (node(next).X, node(next).Y);
        }
        double Total(List<T> r) => Length(r.Select(node));
        var improved = true;
        while (improved && route.Count > 3)
        {
            improved = false;
            for (var i = 0; i < route.Count - 1; i++)
            {
                for (var k = i + 1; k < route.Count; k++)
                {
                    var candidate = route.Take(i).Concat(route.Skip(i).Take(k - i + 1).Reverse()).Concat(route.Skip(k + 1)).ToList();
                    if (Total(candidate) + 0.01 < Total(route))
                    {
                        route = candidate;
                        improved = true;
                    }
                }
            }
        }
        return route;
    }

    public static IReadOnlyList<T> Order<T>(IEnumerable<T> items, Func<T, RouteNode> node, string algorithm) =>
        algorithm == RouteAlgorithms.Baseline ? Baseline(items, node) : Optimized(items, node);

    /// <summary>Упорядочить позиции задания: с известной ячейкой — по алгоритму, без неё — в конце.</summary>
    public static List<TaskItem> Sort(IReadOnlyList<TaskItem> items, IReadOnlyDictionary<string, RouteNode> locations, string algorithm)
    {
        var known = items.Where(i => locations.ContainsKey(i.LocationId));
        return Order(known, i => locations[i.LocationId], algorithm).Concat(items.Where(i => !locations.ContainsKey(i.LocationId))).ToList();
    }
}

/// <summary>Норматив и допустимые роли по типу задания (TASK_TYPES/NORM_PER_UNIT в моках).</summary>
public static class TaskRules
{
    public static readonly Role[] Workers = [Role.FcReceiver, Role.FcPicker, Role.FcPacker, Role.FcSupervisor];

    public static IReadOnlyList<Role> EligibleRoles(TaskType type) => type switch
    {
        TaskType.Receiving or TaskType.Putaway => [Role.FcReceiver, Role.FcSupervisor],
        TaskType.Picking => [Role.FcPicker, Role.FcSupervisor],
        TaskType.Packing => [Role.FcPacker, Role.FcSupervisor],
        TaskType.Move => [Role.FcPicker, Role.FcReceiver, Role.FcSupervisor],
        _ => [Role.FcPicker, Role.FcSupervisor],
    };

    public static string Label(TaskType type) => type switch
    {
        TaskType.Receiving => "Приёмка",
        TaskType.Putaway => "Размещение",
        TaskType.Picking => "Отбор",
        TaskType.Packing => "Упаковка",
        TaskType.Move => "Перемещение",
        _ => "Инвентаризация",
    };

    private static double PerUnit(TaskType type) => type switch
    {
        TaskType.Receiving => 0.25,
        TaskType.Putaway => 0.3,
        TaskType.Picking => 0.6,
        TaskType.Packing => 0.5,
        TaskType.Move => 0.5,
        _ => 0.4,
    };

    public static int NormMinutes(WarehouseTask task) => task.Type == TaskType.Inventory
        ? Math.Max(5, task.Items.Count * 4)
        : Math.Max(3, (int)JsMath.Round(task.Units * PerUnit(task.Type) + task.Items.Count * 0.7));

    /// <summary>Срочно, если хоть один заказ должен уйти в ближайшие 4 часа.</summary>
    public static TaskPriority PriorityFor(IEnumerable<Order> orders, DateTimeOffset now) =>
        orders.Any(o => o.ShipBy < now.AddHours(4)) ? TaskPriority.High : TaskPriority.Normal;
}

/// <summary>Остаток товара на складе для подбора ячейки отбора.</summary>
public sealed record StockSlot(string ProductId, string WarehouseId, string LocationId, int Quantity);

/// <summary>Лист отбора: строки заказов сводятся по (товар, ячейка); ячейка — первая с остатком, иначе любая, иначе зона PCK.</summary>
public static class PickListBuilder
{
    public const int MaxBatchOrders = 12;

    public static List<TaskItem> Build(IEnumerable<Order> orders, IReadOnlyList<StockSlot> stock, Func<string, string?> pickZoneLocation)
    {
        var map = new Dictionary<(string, string), int>();
        var order = new List<(string ProductId, string LocationId)>();
        foreach (var o in orders)
        {
            foreach (var line in o.Lines)
            {
                var slot = stock.FirstOrDefault(s => s.ProductId == line.ProductId && s.WarehouseId == o.WarehouseId && s.Quantity > 0)
                           ?? stock.FirstOrDefault(s => s.ProductId == line.ProductId && s.WarehouseId == o.WarehouseId);
                var locationId = slot?.LocationId ?? pickZoneLocation(o.WarehouseId) ?? string.Empty;
                var key = (line.ProductId, locationId);
                if (!map.ContainsKey(key)) order.Add(key);
                map[key] = map.GetValueOrDefault(key) + line.Quantity;
            }
        }
        return order.Select(k => new TaskItem(k.ProductId, k.LocationId, map[k])).ToList();
    }

    /// <summary>Позиции для упаковки: те же товары, ячейка — зона упаковки, если есть.</summary>
    public static List<TaskItem> ForPacking(IEnumerable<TaskItem> items, string? packLocationId) =>
        items.Select(i => packLocationId is null ? new TaskItem(i.ProductId, i.LocationId, i.Quantity) : new TaskItem(i.ProductId, packLocationId, i.Quantity)).ToList();
}
