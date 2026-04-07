using System.Text.Json;

namespace TradingResearchEngine.Api.Middleware;

/// <summary>
/// Catches all unhandled exceptions and returns HTTP 500 with a CorrelationId.
/// Never includes stack traces in responses.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    /// <inheritdoc cref="ErrorHandlingMiddleware"/>
    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>Invokes the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            var correlationId = Guid.NewGuid().ToString();
            _logger.LogError(ex, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/json";
            var body = JsonSerializer.Serialize(new
            {
                correlationId,
                message = "An unexpected error occurred."
            });
            await context.Response.WriteAsync(body);
        }
    }
}
