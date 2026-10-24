using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static KeyLoad.Analyzers.CodeQuality.ImplementationTermCatalog;

namespace KeyLoad.Analyzers.CodeQuality;

/// <summary>Classifies words and compound identifiers against the canonical implementation vocabulary.</summary>
internal static class ImplementationTermReader
{
    private static readonly Regex Words = new("[A-Z]?[a-z]+|[A-Z]+(?![a-z])", RegexOptions.CultureInvariant);

    internal static IEnumerable<(string Term, string Text)> Read(string text)
    {
        var previous = string.Empty;
        foreach (Match word in Words.Matches(text))
        {
            var value = word.Value.ToUpperInvariant();
            var term = Classify(value, previous);
            term ??= word.Value == value ? Compound(value) : null;
            if (term is not null)
            {
                yield return (term, word.Value);
            }
            previous = value;
        }
    }

    private static string? Classify(string value, string previous) => value switch
    {
        "FALLBACK" or "FALLBACKS" => AlternateWord,
        "BACK" when previous == "FALL" => AlternateWord,
        "MIGRATE" or "MIGRATES" or "MIGRATED" or "MIGRATING" or "MIGRATION" or "MIGRATIONS" => MovementWord,
        "REGRESS" or "REGRESSES" or "REGRESSED" or "REGRESSING" or "REGRESSION" or "REGRESSIONS" => PriorFailureWord,
        "MOCK" or "MOCKS" or "MOCKED" or "MOCKING" => SubstituteWord,
        "FIXTURE" or "FIXTURES" => ResourceWord,
        "SAMPLE" or "SAMPLES" or "SAMPLED" or "SAMPLING" => DataWord,
        "SIMPLE" => DemonstrationWord,
        "COMPATIBILITY" or "COMPATIBILITIES" => OlderContractWord,
        "COMPATIBLE" when previous is "BACKWARD" or "BACKWARDS" => OlderContractWord,
        _ => null
    };

    private static string? Compound(string value) => value switch
    {
        _ when value.Contains("FALLBACK", StringComparison.Ordinal) => AlternateWord,
        _ when value.Contains("MIGRAT", StringComparison.Ordinal) => MovementWord,
        _ when value.Contains("REGRESS", StringComparison.Ordinal) => PriorFailureWord,
        _ when value.Contains("MOCK", StringComparison.Ordinal) => SubstituteWord,
        _ when value.Contains("FIXTURE", StringComparison.Ordinal) => ResourceWord,
        _ when value.Contains("SAMPLE", StringComparison.Ordinal) || value.Contains("SAMPLING", StringComparison.Ordinal) => DataWord,
        _ when value.Contains("SIMPLE", StringComparison.Ordinal) => DemonstrationWord,
        _ when value.Contains("COMPATIBILITY", StringComparison.Ordinal) || value.Contains("BACKWARDCOMPATIBLE", StringComparison.Ordinal) ||
            value.Contains("BACKWARDSCOMPATIBLE", StringComparison.Ordinal) => OlderContractWord,
        _ => null
    };
}
