using System.Collections.Immutable;

namespace KeyLoad;

/// <summary>Reports the commands one storage commit applied, in order, and the canonical sequence after them.</summary>
/// <param name="Position">The committed canonical sequence after the applied commands.</param>
/// <param name="Applied">Results of the applied prefix in submission order; the remaining commands were not applied.</param>
[Orleans.GenerateSerializer]
public sealed record StorageBatchResult(
    [property: Orleans.Id(StorageBatchResult.PositionField)] long Position,
    [property: Orleans.Id(StorageBatchResult.AppliedField)] ImmutableArray<StorageOperationResult> Applied)
{
    internal const int PositionField = 0;
    internal const int AppliedField = 1;
}
