using ManagedCode.Communication;

namespace KeyLoad.Clustering;

/// <summary>One submitted command's state after a copy batch: its result, its failure, or not applied yet.</summary>
/// <param name="Result">The acknowledged result when <see cref="Failure"/> is null and <see cref="Decided"/> is true.</param>
/// <param name="Failure">The failure the caller receives.</param>
/// <param name="Decided">False when no copy applied the command, so it belongs in a later batch.</param>
internal readonly record struct StorageCopyOutcome(Result<object> Result, Exception? Failure, bool Decided)
{
    internal static StorageCopyOutcome Completed(Result<object> result) => new(result, null, Decided: true);

    internal static StorageCopyOutcome Failed(Exception failure) => new(default, failure, Decided: true);
}
