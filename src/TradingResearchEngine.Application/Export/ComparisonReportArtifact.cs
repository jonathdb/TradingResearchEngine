namespace TradingResearchEngine.Application.Export;

/// <summary>
/// The generated comparison report artifact containing Markdown content and optional HTML.
/// </summary>
/// <param name="MarkdownContent">The full Markdown comparison report content.</param>
/// <param name="HtmlContent">HTML version of the report, or null when HTML export is disabled.</param>
/// <param name="OutputPath">File path where the Markdown artifact was persisted.</param>
public sealed record ComparisonReportArtifact(
    string MarkdownContent,
    string? HtmlContent,
    string OutputPath);
