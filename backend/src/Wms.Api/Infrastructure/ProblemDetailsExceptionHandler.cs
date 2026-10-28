using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Wms.Application.Common;
using Wms.Domain.Common;

namespace Wms.Api.Infrastructure;

/// <summary>AppException/DomainException → RFC 7807 ProblemDetails (тот же вид, что problem() в моках).</summary>
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problems, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var problem = exception switch
        {
            AppException app => Problems.Create(app.Status, app.Title, app.Detail, app.Errors),
            DomainException domain => Problems.Create(400, "Некорректный запрос", domain.Message),
            BadHttpRequestException bad => Problems.Create(400, "Некорректный запрос", bad.Message),
            _ => null,
        };
        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception");
            problem = Problems.Create(500, "Внутренняя ошибка сервера");
        }
        context.Response.StatusCode = problem.Status!.Value;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}

public static class Problems
{
    public static ProblemDetails Create(int status, string title, string? detail = null, IDictionary<string, string[]>? errors = null)
    {
        var problem = errors is { Count: > 0 } ? new ValidationProblemDetails(errors) : new ProblemDetails();
        problem.Type = $"https://httpstatuses.io/{status}";
        problem.Title = title;
        problem.Status = status;
        problem.Detail = detail;
        return problem;
    }

    public static Task WriteAsync(HttpContext context, int status, string title, string? detail = null)
    {
        context.Response.StatusCode = status;
        return context.RequestServices.GetRequiredService<IProblemDetailsService>()
            .WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = Create(status, title, detail) }).AsTask();
    }
}
