using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Stores the outcome and operation samples for one target, scenario, and repetition.</summary>
/// <param name="Target">The target name.</param>
/// <param name="Scenario">The workload scenario.</param>
/// <param name="Repetition">The repetition index.</param>
/// <param name="Status">The outcome status recorded by the harness.</param>
/// <param name="Detail">Additional status detail, when present.</param>
/// <param name="Measurement">The aggregate measurements, when the scenario ran.</param>
/// <param name="Samples">The individual operation samples.</param>
public sealed record ComparisonCase(string Target, Scenario Scenario, int Repetition, string Status,
    string? Detail, Measurement? Measurement, [property: JsonRequired] ImmutableArray<OperationSample> Samples);
