using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Administrator-authorized observation of the actual executing node.</summary>
/// <param name="CapturedAt">Node capture time for process counter sampling.</param>
/// <param name="Node">Actual physical status; never implies all voters are healthy.</param>
/// <param name="Admission">Current node command and HTTP occupancy, not throughput.</param>
/// <param name="Storage">Bounded physical file observation.</param>
/// <param name="Http">Actual process HTTP observations.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.AdminNodeSnapshot)]
public sealed record AdminNodeSnapshot([property: Orleans.Id(0)] DateTimeOffset CapturedAt, [property: Orleans.Id(1)] NodeStatus Node, [property: Orleans.Id(2)] NodeAdmissionStatus Admission,
    [property: Orleans.Id(3)] AdminStorageSnapshot Storage, [property: Orleans.Id(4)] AdminHttpSnapshot Http)
{
    /// <summary>Gets the configured voter identity of the executing node, comparable with the reported leader.</summary>
    [Orleans.Id(5)]
    public string? LocalVoter
    {
        get; init;
    }

    /// <summary>Gets the configured voter membership; membership never implies that another voter is healthy.</summary>
    [Orleans.Id(6)]
    public ImmutableArray<string> Voters { get; init; } = [];

    /// <summary>Gets bounded native derived history of this physical node; null when the embedding host provides none.</summary>
    [Orleans.Id(7)]
    public OperationalAnalyticsReport? OperationalAnalytics
    {
        get; init;
    }
}
