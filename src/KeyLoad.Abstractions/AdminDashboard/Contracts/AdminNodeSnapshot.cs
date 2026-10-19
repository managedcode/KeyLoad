using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Administrator-authorized observation of the actual executing node.</summary>
/// <param name="CapturedAt">Node capture time for process counter sampling.</param>
/// <param name="Node">Actual physical status; execution membership supplies each silo status.</param>
/// <param name="Admission">Current node command and HTTP occupancy, not throughput.</param>
/// <param name="Storage">Bounded physical file observation.</param>
/// <param name="Http">Actual process HTTP observations.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.AdminNodeSnapshot)]
public sealed record AdminNodeSnapshot([property: Orleans.Id(0)] DateTimeOffset CapturedAt, [property: Orleans.Id(1)] NodeStatus Node, [property: Orleans.Id(2)] NodeAdmissionStatus Admission,
    [property: Orleans.Id(3)] AdminStorageSnapshot Storage, [property: Orleans.Id(4)] AdminHttpSnapshot Http)
{
    /// <summary>Gets the executing execution Orleans silo name.</summary>
    [Orleans.Id(5)]
    public string? SiloName { get; init; }

    /// <summary>Gets active silo names directly from execution Orleans membership.</summary>
    [Orleans.Id(6)]
    public ImmutableArray<string> ActiveSilos { get; init; } = [];

}
