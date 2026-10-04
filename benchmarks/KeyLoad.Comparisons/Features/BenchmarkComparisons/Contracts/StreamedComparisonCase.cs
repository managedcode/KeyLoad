using System.Text.Json;

namespace KeyLoad.Comparisons;

/// <summary>Write-only case projection which streams its existing sample array.</summary>
internal sealed record StreamedComparisonCase(string Target, Scenario Scenario, int Repetition, string Status,
    string? Detail, Measurement? Measurement, IAsyncEnumerable<OperationSample> Samples)
{
    internal static StreamedComparisonCase Create(ComparisonCase comparisonCase)
    {
        if (comparisonCase.Samples.IsDefault)
        {
            throw new JsonException(ImmutableArrayAsyncView.InvalidCollectionMessage);
        }

        var samples = ImmutableArrayAsyncView.Create(comparisonCase.Samples);
        return new StreamedComparisonCase(comparisonCase.Target, comparisonCase.Scenario, comparisonCase.Repetition,
            comparisonCase.Status, comparisonCase.Detail, comparisonCase.Measurement, samples);
    }
}
