using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Requests acknowledgement of a projection batch and submission of its effects.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer acknowledging the batch.</param>
/// <param name="Token">The batch acknowledgement token.</param>
/// <param name="Effects">The mutations produced by projection processing.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.CommitProjectionBatchRequest)]
public sealed record CommitProjectionBatchRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] ProjectionConsumerRef Consumer, [property: Orleans.Id(2)] string Token, [property: Orleans.Id(3)] ImmutableArray<Mutation> Effects)
{
    /// <summary>Gets the signed proof binding the original full ANN result to this canonical settlement.</summary>
    [Orleans.Id(4), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? InitialAnnMaintenanceSettlement
    {
        get; init;
    }
}
