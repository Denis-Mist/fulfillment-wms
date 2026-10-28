namespace Wms.Domain.Common;

/// <summary>Данные не проходят бизнес-правило (HTTP 422): заголовок и пояснение.</summary>
public sealed class DomainValidationException(string message, string? detail = null) : Exception(message)
{
    public string? Detail { get; } = detail;
}
