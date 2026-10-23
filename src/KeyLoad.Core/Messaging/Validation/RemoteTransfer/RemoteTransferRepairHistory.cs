using System.Security.Cryptography;
using KeyLoad.Core.Features.CommandOutcomes.Contracts;
using KeyLoad.Core.Features.CommandOutcomes.Execution;
using KeyLoad.Core.Features.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes RemoteTransferRepairHistory operations against the caller's database context.</summary>
public static class RemoteTransferRepairHistory
{
    internal static void RequireRemoteTransferRepairHistory(this DatabaseEngine engine, IKeyValueView view, RemoteTransferIntentRecord record, RemoteTransferIntentClaims intent)
    {
        if (record.Repairs is not { } state)
        {
            return;
        }

        RemoteTransferRepairStateValidation.Require(state);
        foreach (var reference in state.History)
        {
            var hint = new RemoteTransferCoordinationHint(record.Source, record.Destination, record.TransferId, record.PrincipalId, record.Fingerprint, RemoteTransferCoordinationIdentity.IntentDigest(record.IntentToken), engine.Token(view, record.Source.Partition, engine.Store.Position), reference.CapacityGeneration, record.Attempts?.Ceiling, reference.PolicyGeneration, reference.CompleteGeneration, state.Ceiling);
            var claims = engine.Verify<RemoteTransferRepairClaims>(reference.WitnessToken, engine.Limits.MaxBatchBytes);
            RemoteTransferRepairWitness.RequireRemoteTransferRepairWitness(claims, hint);
            if (claims.Stage != reference.Stage || claims.FailedCommandId != reference.FailedCommandId || claims.CommandFingerprint != reference.Fingerprint || claims.OutcomeDigest != reference.OutcomeDigest || claims.OwnerCut.Incarnation != intent.Incarnation || reference.CapacityGeneration > (record.Attempts?.Generation ?? RemoteTransferRepairProtocol.InitialGeneration))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid);
            }

            RequireRetainedRemoteTransferRepairOutcome(view, claims);
        }
    }

    internal static void RequireRetainedRemoteTransferRepairOutcome(IKeyValueView view, RemoteTransferRepairClaims claims)
    {
        var partition = claims.Stage == QueueTransferRepairStage.Accept ? claims.Destination.Partition : claims.Source.Partition;
        var selection = CommandOutcomeKeyResolver.Select(view, claims.PrincipalId, claims.FailedCommandId, new(CommandOutcomeScopeKind.Partition, partition));
        var outcome = selection.Outcome;
        var bytes = view.ReadOwnedValue(selection.Key);
        if (outcome is null || bytes is null || outcome.Incarnation != claims.OwnerCut.Incarnation || outcome.PolicyEpoch != claims.OriginalPolicyEpoch || outcome.Fingerprint != claims.CommandFingerprint || Errors.Code(outcome.Result.Problem) != ErrorCode.PermissionDenied || outcome.Result.Value is not null || Convert.ToHexString(SHA256.HashData(bytes)) != claims.OutcomeDigest)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteTransferRepairProtocol.Invalid);
        }
    }
}
