using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Server;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Corrupts only actual persisted authority records and conserves both native owners before exact-byte repair.</summary>
internal static class ResourceLifetimeGrantStoredAuthority
{
    private const long SequenceStep = 1;
    private const string OtherDigest = "0000000000000000000000000000000000000000000000000000000000000000";

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var commandKey = PartitionControlCommandKeys.Authority(state.Record.Identity);
        var command = state.Source.Store.Read(view => view.GetRecord<PartitionControlCommandRecord>(commandKey))
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        await RefuseAsync(state, commandKey, NativeSerialization.Serialize(command with
        {
            AdmissionPosition = checked(command.AdmissionPosition + SequenceStep)
        }), token);
        await RefuseAsync(state, commandKey, NativeSerialization.Serialize(command with
        {
            ControlOwner = command.ControlOwner with
            {
                PhysicalShardId = state.Target.PhysicalOwner.PhysicalShardId
            }
        }), token);
        var grant = state.Proof.OriginalGrant;
        var outcomeKey = KeySpace.PartitionOutcome(grant.Partition, grant.OperatorPrincipalId, grant.GrantId);
        var outcome = state.Source.Store.Read(view => view.GetRecord<StoredOutcome>(outcomeKey))
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var original = outcome.Result.Get<PartitionMovePhaseResult>();
        await RefuseOutcomeAsync(state, outcomeKey, outcome, original with
        {
            MoveId = Guid.NewGuid()
        }, token);
        await RefuseOutcomeAsync(state, outcomeKey, outcome, original with
        {
            Journal = original.Journal with
            {
                PhysicalOwner = state.Target.PhysicalOwner
            }
        }, token);
        await RefuseOutcomeAsync(state, outcomeKey, outcome, original with
        {
            Journal = original.Journal with
            {
                ControlIntentDigest = OtherDigest
            }
        }, token);
    }

    private static Task RefuseOutcomeAsync(ResourceLifetimeGrantState state, byte[] key, StoredOutcome original,
        PartitionMovePhaseResult phase, CancellationToken token)
    {
        var corrupted = original with
        {
            Result = original.Result with
            {
                NativeValue = phase,
                Json = original.Result.Json is null ? null : JsonSerializer.Serialize(phase, JsonDefaults.Options)
            }
        };
        return RefuseAsync(state, key, NativeSerialization.Serialize(corrupted), token);
    }

    private static async Task RefuseAsync(ResourceLifetimeGrantState state, byte[] key, byte[] corrupt, CancellationToken token)
    {
        var original = state.Source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        state.Source.Store.Commit((transaction, _) => { transaction.Put(key, corrupt); return true; });
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
                state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => source.AssertUnchangedAsync(state.Source), failures);
        await ServerFailureObserver.ObserveAsync(() => target.AssertUnchangedAsync(state.Target), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        state.Source.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        await Assert.That(state.Source.Store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(original)).IsTrue();
        await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof,
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
    }
}
