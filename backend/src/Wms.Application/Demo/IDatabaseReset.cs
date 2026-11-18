namespace Wms.Application.Demo;

/// <summary>Низкоуровневый сброс БД: TRUNCATE бизнес-таблиц и повторный сид. Не трогает историю миграций.</summary>
public interface IDatabaseReset
{
    Task TruncateBusinessDataAsync(CancellationToken ct);
    Task SeedAsync(CancellationToken ct);
}
