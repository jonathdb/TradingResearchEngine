using TradingResearchEngine.Application.DataFiles;
using TradingResearchEngine.Application.Strategy;
using TradingResearchEngine.Core.Configuration;

namespace TradingResearchEngine.Application.Engine;

/// <summary>
/// Validates and applies execution window edits (timeframe, start date, end date)
/// to a <see cref="StrategyVersion"/>'s <see cref="ScenarioConfig"/>.
/// </summary>
public static class ExecutionWindowEditor
{
    /// <summary>Result of an execution window edit attempt.</summary>
    public sealed record EditResult(bool Success, IReadOnlyList<string> Errors, StrategyVersion? UpdatedVersion);

    /// <summary>
    /// Validates the proposed execution window and returns an updated version if valid.
    /// Does not persist — the caller is responsible for saving.
    /// </summary>
    public static EditResult Validate(
        StrategyVersion version,
        string? timeframe,
        DateTimeOffset? startDate,
        DateTimeOffset? endDate,
        DataFileRecord? dataFile = null)
    {
        var errors = new List<string>();

        // Date range validation
        if (startDate.HasValue && endDate.HasValue && startDate >= endDate)
            errors.Add("Start date must be before end date.");

        // Validate against data file bounds if available
        if (dataFile is not null)
        {
            if (startDate.HasValue && dataFile.FirstBar.HasValue && startDate < dataFile.FirstBar)
                errors.Add($"Start date {startDate:yyyy-MM-dd} is before data file start {dataFile.FirstBar:yyyy-MM-dd}.");

            if (endDate.HasValue && dataFile.LastBar.HasValue && endDate > dataFile.LastBar)
                errors.Add($"End date {endDate:yyyy-MM-dd} is after data file end {dataFile.LastBar:yyyy-MM-dd}.");
        }

        // Validate against sealed test set
        if (version.SealedTestSet is { IsSealed: true } sealed_)
        {
            var effStart = startDate ?? DateTimeOffset.MinValue;
            var effEnd = endDate ?? DateTimeOffset.MaxValue;

            // The execution window must not shrink to exclude the sealed set entirely
            // (that would make Final Validation impossible)
            if (effEnd <= sealed_.Start)
                errors.Add("End date would exclude the sealed test set entirely.");
        }

        // BarsPerYear mapping from timeframe
        int? barsPerYear = timeframe switch
        {
            "Daily" => 252,
            "H4" => 1512,
            "H1" => 6048,
            "M15" => 24192,
            _ => null
        };

        if (errors.Count > 0)
            return new EditResult(false, errors, null);

        // Build updated config
        var newOpts = new Dictionary<string, object>(version.BaseScenarioConfig.DataProviderOptions);
        if (startDate.HasValue) newOpts["From"] = startDate.Value;
        else newOpts.Remove("From");
        if (endDate.HasValue) newOpts["To"] = endDate.Value;
        else newOpts.Remove("To");

        var updatedConfig = version.BaseScenarioConfig with
        {
            DataProviderOptions = newOpts,
            Timeframe = timeframe,
            BarsPerYear = barsPerYear ?? version.BaseScenarioConfig.BarsPerYear
        };

        var updatedVersion = version with { BaseScenarioConfig = updatedConfig };

        return new EditResult(true, Array.Empty<string>(), updatedVersion);
    }

    /// <summary>
    /// Extracts the current execution window from a version's config.
    /// Returns nulls for legacy configs that don't have explicit values.
    /// </summary>
    public static (string? Timeframe, DateTimeOffset? StartDate, DateTimeOffset? EndDate) GetCurrentWindow(
        StrategyVersion version)
    {
        var config = version.BaseScenarioConfig;
        var timeframe = config.Timeframe;

        // Infer timeframe from BarsPerYear if not explicitly set
        if (timeframe is null)
        {
            timeframe = config.BarsPerYear switch
            {
                252 => "Daily",
                1512 => "H4",
                6048 => "H1",
                24192 => "M15",
                _ => null
            };
        }

        DateTimeOffset? start = null, end = null;
        if (config.DataProviderOptions.TryGetValue("From", out var f) && f is DateTimeOffset df)
            start = df;
        if (config.DataProviderOptions.TryGetValue("To", out var t) && t is DateTimeOffset dt)
            end = dt;

        return (timeframe, start, end);
    }

    /// <summary>
    /// Estimates bar count for a date range and timeframe.
    /// </summary>
    public static int? EstimateBarCount(string? timeframe, DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null || end is null || timeframe is null) return null;
        var days = (end.Value - start.Value).TotalDays;
        if (days <= 0) return 0;

        return timeframe switch
        {
            "Daily" => (int)(days * 252.0 / 365.0),
            "H4" => (int)(days * 6.0 * 252.0 / 365.0),
            "H1" => (int)(days * 24.0 * 252.0 / 365.0),
            "M15" => (int)(days * 96.0 * 252.0 / 365.0),
            _ => null
        };
    }
}
