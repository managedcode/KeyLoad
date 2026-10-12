using System.Runtime.CompilerServices;
using KeyLoad.Client.Features.ClusterRouting;
using ManagedCode.Communication;

namespace KeyLoad.Client;

/// <summary>Composes bounded whole-partition transfers through the existing authenticated movement operation.</summary>
public static class PhysicalShardPartitionMovement
{
    /// <summary>Moves an explicit ordered set of complete atomic partitions to one registered physical owner.</summary>
    /// <param name="client">The authenticated SDK client; each original request independently checks persisted authority.</param>
    /// <param name="requests">One to 64 distinct immutable requests with stable movement IDs and original placement CAS.</param>
    /// <param name="cancellationToken">Original caller cancellation for the complete enumeration.</param>
    /// <returns>Actual settled native results, stopping at the first failed or non-retired movement.</returns>
    /// <remarks>No whole-shard atomicity or durable batch is created. Re-enumerate the same original requests to resume.</remarks>
    public static IAsyncEnumerable<Result<PartitionMoveResult>> MoveWholePartitionsAsync(this KeyLoadClient client,
        IReadOnlyList<PartitionMoveRequest> requests, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var original = PhysicalShardPartitionPlanAdmission.Snapshot(requests);
        cancellationToken.ThrowIfCancellationRequested();
        return ExecuteAsync(client, original, cancellationToken);
    }

    private static async IAsyncEnumerable<Result<PartitionMoveResult>> ExecuteAsync(KeyLoadClient client,
        PartitionMoveRequest[] original, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var request in original)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actual = await client.MovePartitionAsync(request, cancellationToken).ConfigureAwait(false);
            yield return actual;
            if (!actual.IsSuccess || actual.Value?.Phase != PartitionMovePhase.Retired)
            { yield break; }
        }
    }
}
