using System.Text.RegularExpressions;

namespace TradingResearchEngine.Application.Export;

/// <summary>
/// Validates generated Pine Script and MQL exports for structural correctness.
/// Uses robust structural and syntax heuristics to detect common issues
/// before presenting exported code to the user.
/// </summary>
public sealed class ExportValidator
{
    /// <summary>
    /// Validates the structural correctness of exported strategy code.
    /// </summary>
    /// <param name="code">The generated source code to validate.</param>
    /// <param name="format">The target export format determining which validation rules apply.</param>
    /// <returns>A validation result indicating success or specific structural errors.</returns>
    public ExportValidationResult Validate(string code, ExportFormat format)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return ExportValidationResult.Failure(new[]
            {
                new ExportValidationError(null, "content", "Export code is empty or whitespace-only.")
            });
        }

        var errors = new List<ExportValidationError>();

        ValidateBraces(code, errors);

        switch (format)
        {
            case ExportFormat.PineScript:
                ValidatePineScript(code, errors);
                break;
            case ExportFormat.MQL4:
            case ExportFormat.MQL5:
                ValidateMql(code, format, errors);
                break;
        }

        return errors.Count == 0
            ? ExportValidationResult.Success()
            : ExportValidationResult.Failure(errors);
    }

    private static void ValidateBraces(string code, List<ExportValidationError> errors)
    {
        var lines = code.Split('\n');
        int braceDepth = 0;
        int? firstUnmatchedLine = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool inString = false;
            char stringChar = '\0';

            for (int j = 0; j < line.Length; j++)
            {
                char c = line[j];

                // Skip line comments
                if (!inString && j + 1 < line.Length && c == '/' && line[j + 1] == '/')
                    break;

                // Track string state
                if (!inString && (c == '"' || c == '\''))
                {
                    inString = true;
                    stringChar = c;
                }
                else if (inString && c == stringChar && (j == 0 || line[j - 1] != '\\'))
                {
                    inString = false;
                }

                if (!inString)
                {
                    if (c == '{')
                    {
                        braceDepth++;
                    }
                    else if (c == '}')
                    {
                        braceDepth--;
                        if (braceDepth < 0)
                        {
                            firstUnmatchedLine ??= i + 1;
                        }
                    }
                }
            }
        }

        if (braceDepth > 0)
        {
            errors.Add(new ExportValidationError(
                null,
                "braces",
                $"Unmatched opening braces: {braceDepth} unclosed brace(s) detected."));
        }
        else if (braceDepth < 0 || firstUnmatchedLine.HasValue)
        {
            errors.Add(new ExportValidationError(
                firstUnmatchedLine,
                "braces",
                $"Unmatched closing brace detected at line {firstUnmatchedLine}."));
        }
    }

    private static void ValidatePineScript(string code, List<ExportValidationError> errors)
    {
        ValidatePineVersionDirective(code, errors);
        ValidatePineStrategyDeclaration(code, errors);
    }

    private static void ValidatePineVersionDirective(string code, List<ExportValidationError> errors)
    {
        var lines = code.Split('\n');
        bool foundVersion = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//@version=", StringComparison.Ordinal))
            {
                foundVersion = true;

                // Validate version number follows the directive
                var versionPart = trimmed["//@version=".Length..].Trim();
                if (!int.TryParse(versionPart, out int version) || version < 1 || version > 6)
                {
                    errors.Add(new ExportValidationError(
                        i + 1,
                        "version directive",
                        $"Invalid Pine Script version number: '{versionPart}'. Expected 1-6."));
                }

                break;
            }
        }

        if (!foundVersion)
        {
            errors.Add(new ExportValidationError(
                null,
                "version directive",
                "Missing required //@version directive. Pine Script requires a version declaration."));
        }
    }

    private static void ValidatePineStrategyDeclaration(string code, List<ExportValidationError> errors)
    {
        // Look for strategy() call - required for strategy scripts
        var strategyPattern = new Regex(@"\bstrategy\s*\(", RegexOptions.None, TimeSpan.FromSeconds(1));

        if (!strategyPattern.IsMatch(code))
        {
            errors.Add(new ExportValidationError(
                null,
                "strategy declaration",
                "Missing required strategy() declaration. Pine Script strategy exports must include a strategy() call."));
        }
    }

    private static void ValidateMql(string code, ExportFormat format, List<ExportValidationError> errors)
    {
        ValidateMqlOnInit(code, errors);
        ValidateMqlOnTick(code, format, errors);
    }

    private static void ValidateMqlOnInit(string code, List<ExportValidationError> errors)
    {
        var onInitPattern = new Regex(
            @"\b(int\s+)?OnInit\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        if (!onInitPattern.IsMatch(code))
        {
            errors.Add(new ExportValidationError(
                null,
                "OnInit",
                "Missing required OnInit() function. MQL Expert Advisors must define an OnInit handler."));
        }
    }

    private static void ValidateMqlOnTick(string code, ExportFormat format, List<ExportValidationError> errors)
    {
        var onTickPattern = new Regex(
            @"\b(void\s+)?OnTick\s*\(",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        if (!onTickPattern.IsMatch(code))
        {
            var formatName = format == ExportFormat.MQL4 ? "MQL4" : "MQL5";
            errors.Add(new ExportValidationError(
                null,
                "OnTick",
                $"Missing required OnTick() function. {formatName} Expert Advisors must define an OnTick handler."));
        }
    }
}
