using Wms.Domain.Entities;
using Wms.Domain.Enums;

namespace Wms.Domain.Services;

/// <summary>Ячейки склада для заданий по поставке: зона приёмки (RCV) и зона хранения (STR), как в моках.</summary>
public sealed record InboundLocations(string? ReceivingLocationId, IReadOnlyList<string> StorageLocationIds)
{
    public const string ReceivingZoneCode = "RCV";
    public const string StorageZoneCode = "STR";

    /// <summary>Ячейка размещения строки: STR[(idx·7) mod n], как в генераторе заданий моков.</summary>
    public string StorageFor(int lineIndex) =>
        StorageLocationIds.Count == 0 ? string.Empty : StorageLocationIds[lineIndex * 7 % StorageLocationIds.Count];
}

/// <summary>Новое задание с выделенными идентификатором и номером.</summary>
public delegate (string Id, string Number) TaskIdentity();

/// <summary>Генерация и продвижение заданий приёмки/размещения при смене статуса поставки.</summary>
public static class InboundTaskPlanner
{
    /// <summary>Обновляет существующие задания поставки и возвращает новые (если появились).</summary>
    public static IReadOnlyList<WarehouseTask> Plan(InboundShipment shipment, InboundAction action, IReadOnlyCollection<WarehouseTask> existing,
        InboundLocations locations, string organizationId, DateTimeOffset now, TaskIdentity identity)
    {
        var created = new List<WarehouseTask>();
        WarehouseTask? Open(TaskType type) => existing.Concat(created).FirstOrDefault(t => t.Type == type && t.IsOpen);
        WarehouseTask Ensure(TaskType type)
        {
            var task = Open(type);
            if (task is not null) return task;
            var (id, number) = identity();
            task = new WarehouseTask(id, number, type, shipment.WarehouseId, organizationId, now, ItemsFor(shipment, type, locations), shipment.Id);
            created.Add(task);
            return task;
        }

        switch (action)
        {
            case InboundAction.Arrive:
                Ensure(TaskType.Receiving);
                break;
            case InboundAction.StartReceiving:
                Ensure(TaskType.Receiving).Start(now);
                break;
            case InboundAction.CompleteReceiving:
                Ensure(TaskType.Receiving).Complete(now);
                Ensure(TaskType.Putaway);
                break;
            case InboundAction.CompletePlacement:
                Ensure(TaskType.Putaway).Complete(now);
                break;
            case InboundAction.Cancel:
                foreach (var task in existing) task.Cancel(now);
                break;
        }
        return created;
    }

    /// <summary>Позиции задания: приёмка — ожидаемое кол-во в зоне RCV, размещение — годное (не меньше 1) по ячейкам STR.</summary>
    public static IReadOnlyList<TaskItem> ItemsFor(InboundShipment shipment, TaskType type, InboundLocations locations) =>
        shipment.Lines.Select((line, idx) => type == TaskType.Putaway
                ? new TaskItem(line.ProductId, locations.StorageFor(idx), Math.Max(1, line.GoodQty))
                : new TaskItem(line.ProductId, locations.ReceivingLocationId ?? string.Empty, line.ExpectedQty))
            .ToList();
}

/// <summary>Зачисление годного товара на остатки при завершении размещения.</summary>
public static class InboundPlacement
{
    /// <summary>
    /// Как в моках: годное количество добавляется к первой строке остатка товара на складе,
    /// иначе создаётся новая строка в ячейке размещения. Возвращает новые строки остатка.
    /// </summary>
    public static IReadOnlyList<StockItem> Place(InboundShipment shipment, IReadOnlyList<StockItem> warehouseStock,
        InboundLocations locations, Func<string, string> newStockId)
    {
        var created = new List<StockItem>();
        for (var idx = 0; idx < shipment.Lines.Count; idx++)
        {
            var line = shipment.Lines[idx];
            var good = line.GoodQty;
            if (good <= 0) continue;
            var existing = warehouseStock.Concat(created).FirstOrDefault(s => s.ProductId == line.ProductId && s.WarehouseId == shipment.WarehouseId);
            if (existing is not null) existing.Receive(good);
            else created.Add(new StockItem(newStockId(line.ProductId), line.ProductId, shipment.SellerId, shipment.WarehouseId,
                locations.StorageFor(idx), good, 0));
        }
        return created;
    }
}
