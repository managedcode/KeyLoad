using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Orleans;

/// <summary>Borrowed configured-peer transport for the original separately authorized movement phase.</summary>
internal interface IPartitionMovementDispatcher
{
    /// <summary>Dispatches once; capture continuation is pinned to its authenticated original voter.</summary>
    /// <param name="phaseCommandId">Original immutable receiver command identity.</param>
    /// <param name="original">Exact original admitted envelope and absolute expiry.</param>
    /// <param name="authorization">Actual A grant journal; absent only for local control admission.</param>
    /// <param name="action">Closed transport action.</param>
    /// <param name="handleId">Original capture handle for page or release.</param>
    /// <param name="ordinal">Exact page ordinal; zero for non-page actions.</param>
    /// <param name="pinnedVoter">Authenticated capture voter; mandatory for page and release.</param>
    /// <param name="cancellationToken">Original caller cancellation.</param>
    Task<PartitionMovementDispatchResult> DispatchAsync(Guid phaseCommandId,
        PartitionMovePeerEnvelope original, PartitionMoveJournalReceipt? authorization,
        PartitionMovementPeerAction action, Guid handleId, int ordinal, string? pinnedVoter,
        CancellationToken cancellationToken);
}
