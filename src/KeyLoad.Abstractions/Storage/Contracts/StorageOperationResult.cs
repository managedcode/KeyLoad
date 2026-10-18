namespace KeyLoad;

/// <summary>Reports the actual canonical database sequence and original stored command result.</summary>
/// <param name="Sequence">The sequence persisted with the command; zero identifies work with no canonical database commit.</param>
/// <param name="Result">The original freshly authorized command result.</param>
[Orleans.GenerateSerializer]
public sealed record StorageOperationResult(
    [property: Orleans.Id(StorageOperationResult.SequenceField)] long Sequence,
    [property: Orleans.Id(StorageOperationResult.ResultField)] OperationResult Result)
{
    internal const int SequenceField = 0;
    internal const int ResultField = 1;
}
