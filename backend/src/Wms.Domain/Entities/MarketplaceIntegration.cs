using System.Text.RegularExpressions;
using Wms.Domain.Common;
using Wms.Domain.Enums;

namespace Wms.Domain.Entities;

public sealed record IntegrationLogEntry(string Id, DateTimeOffset At, IntegrationLogKind Kind, string Message, int? Count = null);

/// <summary>Подключение кабинета селлера к маркетплейсу (WB / Ozon).</summary>
public sealed partial class MarketplaceIntegration
{
    public const int LogLimit = 30;
    public const int ErrorLimit = 1000;
    public static readonly int[] Intervals = [5, 15, 30, 60];
    private readonly List<IntegrationLogEntry> _log = [];

    private MarketplaceIntegration() { }

    public MarketplaceIntegration(string organizationId, IntegrationChannel channel)
    {
        OrganizationId = organizationId; Channel = channel; Status = IntegrationStatus.Disconnected;
        SyncIntervalMin = 15; Environment = MarketplaceEnvironment.Sandbox;
    }

    public string OrganizationId { get; private set; } = null!;
    public IntegrationChannel Channel { get; private set; }
    public IntegrationStatus Status { get; private set; }
    public string? KeyMask { get; private set; }
    public string? ClientId { get; private set; }
    public DateTimeOffset? ConnectedAt { get; private set; }
    public DateTimeOffset? LastSyncAt { get; private set; }
    public bool AutoImportFbs { get; private set; }
    public bool PushStock { get; private set; }
    public int SyncIntervalMin { get; private set; }
    public MarketplaceEnvironment Environment { get; private set; }
    public string? LastError { get; private set; }
    public int? LastHttpStatus { get; private set; }
    /// <summary>Зашифрованный ключ API; в DTO не попадает.</summary>
    public string? ApiKeyCipher { get; private set; }
    public IReadOnlyList<IntegrationLogEntry> Log => _log;

    public bool HasApiKey => !string.IsNullOrEmpty(ApiKeyCipher);

    /// <summary>Восстановление демо-состояния (сид).</summary>
    public void Restore(string keyMask, string? clientId, DateTimeOffset connectedAt, DateTimeOffset lastSyncAt, bool pushStock, IEnumerable<IntegrationLogEntry> log)
    {
        Status = IntegrationStatus.Connected; KeyMask = keyMask; ClientId = clientId; ConnectedAt = connectedAt; LastSyncAt = lastSyncAt;
        AutoImportFbs = true; PushStock = pushStock; Environment = MarketplaceEnvironment.Sandbox;
        ApiKeyCipher = null; LastError = null; LastHttpStatus = null;
        _log.Clear();
        _log.AddRange(log.Take(LogLimit));
    }

    public void Connect(string? apiKey, string? clientId, DateTimeOffset now, MarketplaceEnvironment environment = MarketplaceEnvironment.Sandbox)
    {
        var key = apiKey?.Trim() ?? string.Empty;
        if (key.Length < 20) throw new DomainValidationException("Ключ API слишком короткий", "Скопируйте ключ целиком из личного кабинета");
        var client = clientId?.Trim() ?? string.Empty;
        if (Channel == IntegrationChannel.Ozon && !ClientIdPattern().IsMatch(client))
            throw new DomainValidationException("Укажите Client ID", "Client ID Ozon — число из настроек Seller API");
        Status = IntegrationStatus.Connected;
        KeyMask = $"••••{key[^4..]}";
        ClientId = Channel == IntegrationChannel.Ozon ? client : null;
        ConnectedAt = now; LastSyncAt = now; AutoImportFbs = true;
        Environment = environment;
        LastError = null; LastHttpStatus = null;
        Prepend([new IntegrationLogEntry($"{Prefix}_{now.ToUnixTimeMilliseconds()}", now, IntegrationLogKind.Connect, "Кабинет подключён")]);
    }

    public void SetApiKeyCipher(string? cipher) => ApiKeyCipher = cipher;

    public void Disconnect()
    {
        Status = IntegrationStatus.Disconnected; KeyMask = null; ClientId = null; AutoImportFbs = false; PushStock = false;
        ApiKeyCipher = null; LastError = null; LastHttpStatus = null;
    }

    public void Sync(int orders, int skus, DateTimeOffset now) =>
        RecordSyncSuccess(orders, PushStock ? skus : null, now, null);

    public void RecordSyncSuccess(int orders, int? pushedSkus, DateTimeOffset now, int? httpStatus, string? stockNote = null)
    {
        EnsureConnected();
        var ms = now.ToUnixTimeMilliseconds();
        var entries = new List<IntegrationLogEntry>();
        if (stockNote is not null)
            entries.Add(new($"{Prefix}_{ms}_s", now, IntegrationLogKind.Stock, Clip(stockNote), pushedSkus));
        else if (pushedSkus is int skus)
            entries.Add(new($"{Prefix}_{ms}_s", now, IntegrationLogKind.Stock, $"Остатки выгружены: {skus} SKU", skus));
        entries.Add(new($"{Prefix}_{ms}_o", now, IntegrationLogKind.Orders, $"Заказов FBS в кабинете: {orders}", orders));
        Status = IntegrationStatus.Connected;
        LastSyncAt = now; LastError = null; LastHttpStatus = httpStatus;
        Prepend(entries);
    }

    public void RecordSyncFailure(string message, int? httpStatus, DateTimeOffset now)
    {
        EnsureConnected();
        var text = Clip(string.IsNullOrWhiteSpace(message) ? "Ошибка API маркетплейса" : message.Trim());
        Status = IntegrationStatus.Error;
        LastError = text;
        LastHttpStatus = httpStatus;
        Prepend([new IntegrationLogEntry($"{Prefix}_{now.ToUnixTimeMilliseconds()}_e", now, IntegrationLogKind.Error, text)]);
    }

    public void UpdateSettings(bool autoImportFbs, bool pushStock, int syncIntervalMin, MarketplaceEnvironment? environment = null)
    {
        if (!Intervals.Contains(syncIntervalMin)) throw new DomainValidationException("Недопустимый интервал");
        EnsureConnected();
        AutoImportFbs = autoImportFbs; PushStock = pushStock; SyncIntervalMin = syncIntervalMin;
        if (environment is { } env) Environment = env;
    }

    private string Prefix => Channel == IntegrationChannel.Wb ? "wb" : "ozon";

    private static string Clip(string text) => text.Length <= ErrorLimit ? text : text[..ErrorLimit];

    private void EnsureConnected()
    {
        if (Status == IntegrationStatus.Disconnected) throw new DomainConflictException("Кабинет не подключён");
    }

    private void Prepend(IEnumerable<IntegrationLogEntry> entries)
    {
        _log.InsertRange(0, entries);
        if (_log.Count > LogLimit) _log.RemoveRange(LogLimit, _log.Count - LogLimit);
    }

    [GeneratedRegex(@"^\d{4,10}$")]
    private static partial Regex ClientIdPattern();
}
