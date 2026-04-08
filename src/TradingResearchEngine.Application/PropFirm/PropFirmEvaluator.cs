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

    /// <summary>
    /// Evaluates a backtest result against a <see cref="PropFirmRulePack"/> with per-phase,
    /// per-rule results including margin and near-breach detection.
    /// </summary>
    public IReadOnlyList<PhaseEvaluationResult> EvaluateRulePack(BacktestResult result, PropFirmRulePack rulePack)
    {
        var phaseResults = new List<PhaseEvaluationResult>();

        foreach (var phase in rulePack.Phases)
        {
            var rules = new List<RuleResult>();

            // Profit target
            decimal totalReturn = result.StartEquity > 0m
                ? (result.EndEquity - result.StartEquity) / result.StartEquity * 100m
                : 0m;
            rules.Add(EvaluateRule("Profit Target", totalReturn, phase.ProfitTargetPercent, higherIsBetter: true));

            // Max daily drawdown (approximated from max drawdown — daily DD tracking would need per-day equity)
            decimal maxDdPercent = result.MaxDrawdown * 100m;
            rules.Add(EvaluateRule("Max Daily Drawdown", maxDdPercent, phase.MaxDailyDrawdownPercent, higherIsBetter: false));

            // Max total drawdown
            rules.Add(EvaluateRule("Max Total Drawdown", maxDdPercent, phase.MaxTotalDrawdownPercent, higherIsBetter: false));

            // Min trading days
            rules.Add(EvaluateRule("Min Trading Days", result.TotalTrades, phase.MinTradingDays, higherIsBetter: true));

            // Max trading days (if applicable)
            if (phase.MaxTradingDays.HasValue)
            {
                var equityCurveDays = result.EquityCurve.Count;
                rules.Add(EvaluateRule("Max Trading Days", equityCurveDays, phase.MaxTradingDays.Value, higherIsBetter: false));
            }

            // Consistency rule
            if (phase.ConsistencyRulePercent.HasValue)
            {
                var totalProfit = result.Trades.Where(t => t.NetPnl > 0).Sum(t => t.NetPnl);
                decimal consistencyPercent = 0m;
                if (totalProfit > 0m)
                {
                    var maxSingleTrade = result.Trades.Max(t => t.NetPnl);
                    consistencyPercent = maxSingleTrade / totalProfit * 100m;
                }
                rules.Add(EvaluateRule("Consistency", consistencyPercent, phase.ConsistencyRulePercent.Value, higherIsBetter: false));
            }

            bool phasePassed = rules.All(r => r.Status != RuleStatus.Failed);
            phaseResults.Add(new PhaseEvaluationResult(phase.PhaseName, phasePassed, rules));
        }

        return phaseResults;
    }

    private static RuleResult EvaluateRule(string name, decimal actual, decimal limit, bool higherIsBetter)
    {
        decimal margin;
        RuleStatus status;

        if (higherIsBetter)
        {
            margin = actual - limit;
            status = actual >= limit ? RuleStatus.Passed
                : actual >= limit * 0.8m ? RuleStatus.NearBreach
                : RuleStatus.Failed;
        }
        else
        {
            margin = limit - actual;
            status = actual <= limit ? RuleStatus.Passed
                : actual <= limit * 1.2m ? RuleStatus.NearBreach
                : RuleStatus.Failed;
        }

        // Near-breach override: if passed but within 20% of limit
        if (status == RuleStatus.Passed && Math.Abs(margin) < Math.Abs(limit) * 0.2m)
            status = RuleStatus.NearBreach;

        return new RuleResult(name, status, actual, limit, margin);
    }
}
