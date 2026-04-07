using Microsoft.Extensions.Logging;
using TradingResearchEngine.Application.PropFirm.Results;
using TradingResearchEngine.Core.Results;

namespace TradingResearchEngine.Application.PropFirm;

/// <summary>
/// Evaluates BacktestResults against firm rules and computes prop-firm economics.
/// All formulas are pure functions — no side effects beyond logging.
/// </summary>
public sealed class PropFirmEvaluator
{
    private readonly ILogger<PropFirmEvaluator> _logger;

    /// <inheritdoc cref="PropFirmEvaluator"/>
    public PropFirmEvaluator(ILogger<PropFirmEvaluator> logger) => _logger = logger;

    /// <summary>
    /// Evaluates a backtest result against a firm rule set.
    /// Returns a report indicating pass/fail for each rule.
    /// </summary>
    public RuleEvaluationReport Evaluate(BacktestResult result, FirmRuleSet rules)
    {
        var violations = new List<string>();

        if (result.MaxDrawdown > rules.MaxTotalDrawdownPercent / 100m)
            violations.Add($"MaxTotalDrawdown exceeded: {result.MaxDrawdown:P2} > {rules.MaxTotalDrawdownPercent}%");

        if (rules.MinTradingDays > 0 && result.TotalTrades < rules.MinTradingDays)
            violations.Add($"MinTradingDays not met: {result.TotalTrades} < {rules.MinTradingDays}");

        if (rules.ConsistencyRulePercent.HasValue)
        {
            // Consistency: no single trade should account for more than X% of total profit
            var totalProfit = result.Trades.Where(t => t.NetPnl > 0).Sum(t => t.NetPnl);
            if (totalProfit > 0)
            {
                var maxSingleTrade = result.Trades.Max(t => t.NetPnl);
                var singleTradePercent = maxSingleTrade / totalProfit * 100m;
                if (singleTradePercent > rules.ConsistencyRulePercent.Value)
                    violations.Add($"Consistency rule violated: single trade is {singleTradePercent:F2}% of total profit");
            }
        }

        var passed = violations.Count == 0;
        var outcome = passed ? "Passed" : "Failed";
        return new RuleEvaluationReport(passed, outcome, violations);
    }

    /// <summary>
    /// Computes economics for an instant-funding configuration.
    /// </summary>
    public PropFirmScenarioResult ComputeEconomics(InstantFundingConfig config, string presetName = "Base")
    {
        var challengeProb = config.DirectFundedProbabilityPercent / 100m;

        var monthlyPayout = config.NotionalSizeUsd
            * (config.GrossMonthlyReturnPercent / 100m)
            * (config.PayoutSplitPercent / 100m)
            * config.PayoutFrictionFactor;

        var lifetimeEV = (monthlyPayout * config.ExpectedPayoutMonths) - config.AccountFeeUsd;

        int? breakevenMonths;
        if (monthlyPayout <= 0)
        {
            breakevenMonths = null;
            _logger.LogWarning("MonthlyPayoutExpectancy is {Payout}; BreakevenMonths set to null.", monthlyPayout);
        }
        else
        {
            breakevenMonths = (int)Math.Ceiling(config.AccountFeeUsd / monthlyPayout);
        }

        return new PropFirmScenarioResult(presetName, challengeProb, monthlyPayout, lifetimeEV, breakevenMonths);
    }

    /// <summary>
    /// Computes challenge probability from a ChallengeConfig.
    /// </summary>
    public decimal ComputeChallengeProbability(ChallengeConfig config)
    {
        return config.PassRatePercent * config.PassToFundedConversionPercent / 10000m;
    }
}
