namespace Wms.Application.Common;

/// <summary>Ошибка прикладного уровня; Api превращает её в ProblemDetails с тем же статусом.</summary>
public class AppException(int status, string title, string? detail = null,
    IDictionary<string, string[]>? errors = null) : Exception(detail ?? title)
{
    public int Status { get; } = status;
    public string Title { get; } = title;
    public string? Detail { get; } = detail;
    public IDictionary<string, string[]>? Errors { get; } = errors;

    public static AppException BadRequest(string title, string? detail = null, IDictionary<string, string[]>? errors = null) => new(400, title, detail, errors);
    public static AppException Unauthorized(string title = "Требуется авторизация", string? detail = null) => new(401, title, detail);
    public static AppException Forbidden(string title = "Нет доступа к организации", string? detail = null) => new(403, title, detail);
    public static AppException InsufficientRights() => new(403, "Недостаточно прав");
    public static AppException NotFound(string title, string? detail = null) => new(404, title, detail);
    public static AppException Conflict(string title, string? detail = null) => new(409, title, detail);
    public static AppException Unprocessable(string title, string? detail = null) => new(422, title, detail);
}
