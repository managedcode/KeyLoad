using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Mutates carriers, never authority, and requires complete original owner images/cuts after each rejection.</summary>
internal static class ResourceLifetimeGrantRefusals
{
    private const long SequenceStep = 1;
    private const string GrantProofSpace = "partition-move-lifetime-proof-v1";
    internal const string OtherFingerprint = "0000000000000000000000000000000000000000000000000000000000000000";

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        await ResourceLifetimeGrantNativeFormat.ExecuteAsync(state, token);
        await ResourceLifetimeGrantTransportRefusals.LegacyTransportAsync(state, token);
        await ResourceLifetimeGrantStages.MetadataFenceAsync(state, token);
        var proof = state.Proof;
        var coverage = proof.ControlProof.Coverage;
        var first = coverage.Lifetimes.First();
        await ResourceLifetimeGrantCoverageRefusals.ExecuteAsync(state, token);
        await ResourceLifetimeGrantTransportRefusals.TransportAsync(state, token);
        await StoredFingerprintAsync(state, token);
        var delegation = proof.ControlProof.OriginalDelegation;
        await RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                OriginalDelegation = delegation with
                {
                    ControlAdmissionPosition = checked(delegation.ControlAdmissionPosition + SequenceStep)
                }
            }
        }, ErrorCode.OwnershipLost, token);
        await RejectProofAsync(state, proof with
        {
            ControlProof = proof.ControlProof with
            {
                OriginalDelegation = delegation with
                {
                    Principal = delegation.Principal with
                    {
                        PolicyEpoch = checked(delegation.Principal.PolicyEpoch + SequenceStep)
                    }
                }
            }
        }, ErrorCode.OwnershipLost, token);
        await SourcePairAsync(state, first, token);
        await CallerCancellationAsync(state, token);
        var expired = state.Request with
        {
            Envelope = state.Request.Envelope with
            {
                ExpiresAt = state.Target.Database.EvaluationClock.GetUtcNow()
            }
        };
        await RejectAsync(state, () => ControlledPartitionMovementVerifiedSubmit.SubmitAsync(state.Target,
            state.TargetOptions, state.TargetAdmission, expired, state.Proof, token), ErrorCode.Unauthenticated);
    }

    internal static PartitionMovementTransportRequest FreshRequest(ResourceLifetimeGrantState state)
        => state.Request with
        {
            Envelope = state.Request.Envelope with
            {
                Nonce = Guid.NewGuid()
            }
        };

    internal static Task RejectProofAsync(ResourceLifetimeGrantState state, PartitionMoveLifetimeProofV1 proof,
        ErrorCode code, CancellationToken token)
        => RejectAsync(state, () => ControlledPartitionMovementVerifiedSubmit.SubmitAsync(state.Target,
            state.TargetOptions, state.TargetAdmission, FreshRequest(state), proof, token), code);

    internal static async Task RejectAsync(ResourceLifetimeGrantState state, Func<Task<OperationResult>> operation, ErrorCode code)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            try
            {
                var result = await operation();
                await Assert.That(result.Error).IsEqualTo(code);
            }
            catch (KeyLoadException failure)
            {
                await Assert.That(failure.Code).IsEqualTo(code);
            }
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => source.AssertUnchangedAsync(state.Source), failures);
        await ServerFailureObserver.ObserveAsync(() => target.AssertUnchangedAsync(state.Target), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task StoredFingerprintAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var grant = state.Proof.OriginalGrant;
        var key = KeySpace.Partition(GrantProofSpace, grant.Partition, grant.GrantId);
        var original = state.Source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        var corrupt = NativeSerialization.Serialize(state.Proof with
        {
            AuthorizationFingerprint = OtherFingerprint
        });
        state.Source.Store.Commit((transaction, _) => { transaction.Put(key, corrupt); return true; });
        await RejectAsync(state, () =>
        {
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token);
            throw new InvalidOperationException("A corrupt persisted authorization fingerprint was accepted.");
        }, ErrorCode.Corruption);
        state.Source.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        await Assert.That(state.Source.Store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(original)).IsTrue();
        await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof,
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
    }

    private static async Task SourcePairAsync(ResourceLifetimeGrantState state, ResourceLifetimeV1 lifetime, CancellationToken token)
    {
        var key = ResourceLifetimeCatalog.Key(lifetime.TenantId, lifetime.DatabaseId, lifetime.ResourceName);
        var original = state.Source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(PartitionMoveProtocol.MissingAuthority);
        state.Source.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
        state.Source.Store.Commit((transaction, _) => { transaction.Put(key, original); return true; });
        await Assert.That(state.Source.Store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(original)).IsTrue();
        await ResourceLifetimeGrantAssertions.EqualAsync(state.Proof,
            state.Source.Database.CaptureControlledCommandLifetimeProof(state.Request.Envelope, token));
    }

    private static async Task CallerCancellationAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        await caller.CancelAsync();
        Exception? observed = null;
        try
        {
            await ControlledPartitionMovementVerifiedSubmit.SubmitAsync(state.Target, state.TargetOptions,
                state.TargetAdmission, FreshRequest(state), state.Proof, caller.Token);
        }
        catch (OperationCanceledException failure)
        {
            observed = failure;
        }
        await Assert.That(observed).IsNotNull();
        await Assert.That(((OperationCanceledException)observed!).CancellationToken).IsEqualTo(caller.Token);
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
        await ResourceLifetimeGrantAssertions.PinnedAsync(state);
    }
}
