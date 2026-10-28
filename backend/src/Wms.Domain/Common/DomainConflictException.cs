namespace Wms.Domain.Common;

/// <summary>Операция недопустима для текущего состояния сущности (HTTP 409).</summary>
public sealed class DomainConflictException(string message) : Exception(message);
