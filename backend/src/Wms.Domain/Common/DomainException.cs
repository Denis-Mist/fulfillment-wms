namespace Wms.Domain.Common;

/// <summary>Нарушение доменного правила.</summary>
public sealed class DomainException(string message) : Exception(message);
