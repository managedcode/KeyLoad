using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.Server;

internal sealed partial class BackgroundStorageOperations
{
    public Task<SampleChunkWorkHint?> ReadSampleChunkCurrentAsync(SampleChunkWorkHint hint,
        CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunLocalAsync(storage, token =>
        {
            ArgumentNullException.ThrowIfNull(hint);
            SampleChunkJobValidation.Require(hint, hint.Partition.AtomicPartitionId);
            return SampleChunkWorkRead.Current(Database, hint, token);
        }, cancellationToken);

    public Task<SampleChunkWorkPage> ReadSampleChunkWorkPageAsync(byte[]? after,
        CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunLocalAsync(storage,
            token => SampleChunkWorkDiscovery.Read(Database, after, token), cancellationToken);

    public Task<PrincipalRecord> ReadSampleChunkCreatorAsync(SampleChunkWorkHint hint,
        CancellationToken cancellationToken)
        => BackgroundStorageReadExecution.RunLocalAsync(storage, token =>
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(hint);
            SampleChunkJobValidation.Require(hint, hint.Partition.AtomicPartitionId);
            var principal = Database.Store.Read(view => Database.Principal(view, hint.Creator, clock.GetUtcNow()));
            if (principal.PolicyEpoch != hint.CreatorPolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.RevisionConflict, SampleChunkJobProtocol.Invalid);
            }
            return principal;
        }, cancellationToken);
}
