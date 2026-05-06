using TradingResearchEngine.Application.Export;
using TradingResearchEngine.Application.Strategy;

namespace TradingResearchEngine.Api.Endpoints;

/// <summary>Maps strategy-related API endpoints.</summary>
public static class StrategyEndpoints
{
    /// <summary>Registers all strategy endpoints on the route builder.</summary>
    public static void MapStrategyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/strategies/{versionId}/export", async (
            string versionId,
            HttpContext httpContext,
            IStrategyRepository strategyRepository,
            IEnumerable<IStrategyExporter> exporters,
            CancellationToken ct) =>
        {
            // Validate format query parameter
            var formatStr = httpContext.Request.Query["format"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(formatStr))
            {
                return Results.BadRequest(new
                {
                    errors = new[] { new { field = "format", message = "The 'format' query parameter is required. Valid values: MQL4, MQL5, PineScript." } }
                });
            }

            if (!Enum.TryParse<ExportFormat>(formatStr, ignoreCase: true, out var format))
            {
                return Results.BadRequest(new
                {
                    errors = new[] { new { field = "format", message = $"Invalid format '{formatStr}'. Valid values: MQL4, MQL5, PineScript." } }
                });
            }

            // Resolve strategy version
            var version = await strategyRepository.GetVersionAsync(versionId, ct);
            if (version is null)
            {
                return Results.BadRequest(new
                {
                    errors = new[] { new { field = "versionId", message = $"Strategy version '{versionId}' not found." } }
                });
            }

            // Find the appropriate exporter
            var exporter = exporters.FirstOrDefault(e => e.Format == format);
            if (exporter is null)
            {
                return Results.BadRequest(new
                {
                    errors = new[] { new { field = "format", message = $"No exporter registered for format '{format}'." } }
                });
            }

            var result = await exporter.ExportAsync(version, ct);
            return Results.Text(result.Code, "text/plain");
        }).WithName("ExportStrategy").WithTags("Strategies")
          .Produces(StatusCodes.Status200OK, contentType: "text/plain")
          .Produces(StatusCodes.Status400BadRequest);
    }
}
