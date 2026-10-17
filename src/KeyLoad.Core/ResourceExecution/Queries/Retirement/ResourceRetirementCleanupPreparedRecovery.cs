using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireCleanupCheckpointOriginal(IKeyValueView view, ResourceRetirementCleanupCheckpointV1 checkpoint)
    {
        var prepared = ResourceRetirementPreparedOperationStorage.ReadCleanupCheckpoint(view, checkpoint, Limits);
        RequirePreparedResourceRetirementOriginal(prepared);
        if (prepared.Stage != ResourceRetirementPreparedOperationStage.Persisted || prepared.Original is null
            || prepared.Original.Id != checkpoint.Receipt.CommandId
            || checkpoint.OutcomeIdentity is null
            || checkpoint.OutcomeIdentity.PrincipalId != prepared.Original.PrincipalId
            || checkpoint.OutcomeIdentity.Fingerprint != NativeOperationFingerprint.Compute(prepared.Original))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
    }
}
