using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireRemoteTransferRepairHistory(IKeyValueView view, RemoteTransferIntentRecord record,
        RemoteTransferIntentClaims intent)
    {
        if (record.Repairs is not { } state)
        { return; }
        RemoteTransferRepairStateValidation.Require(state);
        foreach (var reference in state.History)
        {
            var hint = new RemoteTransferCoordinationHint(record.Source, record.Destination, record.TransferId,
                record.PrincipalId, record.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken),
                Token(view, record.Source.Partition, Store.Position), reference.CapacityGeneration, record.Attempts?.Ceiling,
                reference.PolicyGeneration, reference.CompleteGeneration, state.Ceiling);
            var claims = Verify<RemoteTransferRepairClaims>(reference.WitnessToken, Limits.MaxBatchBytes);
            RequireRemoteTransferRepairWitness(claims, hint);
            if (claims.Stage != reference.Stage || claims.FailedCommandId != reference.FailedCommandId
                || claims.CommandFingerprint != reference.Fingerprint || claims.OutcomeDigest != reference.OutcomeDigest
                || claims.OwnerCut.Incarnation != intent.Incarnation
                || reference.CapacityGeneration > (record.Attempts?.Generation ?? RemoteTransferRepairProtocol.InitialGeneration))
            { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid); }
            RequireRetainedRemoteTransferRepairOutcome(view, claims);
        }
    }

    private static void RequireRetainedRemoteTransferRepairOutcome(IKeyValueView view, RemoteTransferRepairClaims claims)
    {
        var partition = claims.Stage == QueueTransferRepairStage.Accept ? claims.Destination.Partition : claims.Source.Partition;
        var selection = CommandOutcomeKeyResolver.Select(view, claims.PrincipalId, claims.FailedCommandId,
            new(CommandOutcomeScopeKind.Partition, partition));
        var outcome = selection.Outcome;
        var bytes = view.ReadOwnedValue(selection.Key);
        if (outcome is null || bytes is null || outcome.Incarnation != claims.OwnerCut.Incarnation
            || outcome.PolicyEpoch != claims.OriginalPolicyEpoch || outcome.Fingerprint != claims.CommandFingerprint
            || outcome.Result is null || outcome.Result.Error != ErrorCode.PermissionDenied || outcome.Result.Json is not null
            || outcome.Result.NativeValue is not null || Convert.ToHexString(SHA256.HashData(bytes)) != claims.OutcomeDigest)
        { throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid); }
    }
}
