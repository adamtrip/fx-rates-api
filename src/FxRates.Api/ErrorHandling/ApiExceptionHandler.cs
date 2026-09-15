using System.Text.Json;
using FxRates.Application.Errors;
using Microsoft.AspNetCore.Diagnostics;

namespace FxRates.Api.ErrorHandling;

/// <summary>Expected failures expose caller-safe messages; unexpected failures get a generic body and a trace ID.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken token)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            // Use nginx's 499 convention for disconnected clients; there is no receiver for a body.
            context.Response.StatusCode = 499;
            return true;
        }

        var (status, title, detail) = exception switch
        {
            FxException e => (StatusFor(e.Kind), TitleFor(e.Kind), e.Message),
            // Converter and parser messages are written for callers; other binding failures get a generic hint.
            BadHttpRequestException { InnerException: JsonException json } e => (e.StatusCode, "Invalid request", json.Message),
            BadHttpRequestException e => (e.StatusCode, "Invalid request", "Check the request body and content type."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "The request could not be completed.")
        };

        if (status >= 500)
        {
            // Expected provider failures are not application faults, so log them as warnings with the caller's trace ID.
            if (exception is FxException)
            {
                logger.LogWarning(exception, "Request {Method} {Path} failed with {StatusCode}; trace {TraceId}",
                    context.Request.Method, context.Request.Path, status, context.TraceIdentifier);
            }
            else
            {
                logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}; trace {TraceId}",
                    context.Request.Method, context.Request.Path, status, context.TraceIdentifier);
            }
        }

        await Results.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
        return true;
    }

    private static int StatusFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.AlreadyExists => StatusCodes.Status409Conflict,
        ErrorKind.ConcurrentChange => StatusCodes.Status409Conflict,
        ErrorKind.ProviderFailure => StatusCodes.Status502BadGateway,
        ErrorKind.ProviderUnavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorKind.ProviderTimeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError
    };

    private static string TitleFor(ErrorKind kind) => kind switch
    {
        ErrorKind.Validation => "Invalid rate",
        ErrorKind.NotFound => "Rate not found",
        ErrorKind.AlreadyExists => "Rate already exists",
        ErrorKind.ConcurrentChange => "Rate changed concurrently",
        ErrorKind.ProviderFailure => "Invalid provider response",
        ErrorKind.ProviderUnavailable => "Provider unavailable",
        ErrorKind.ProviderTimeout => "Provider timeout",
        _ => "Unexpected error"
    };
}
