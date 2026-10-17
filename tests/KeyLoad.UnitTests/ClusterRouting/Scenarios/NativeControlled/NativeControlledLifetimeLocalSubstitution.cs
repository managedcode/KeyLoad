using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Uses a real persisted source row to prove destination-local bytes cannot replace authenticated coverage.</summary>
internal static class NativeControlledLifetimeLocalSubstitution
{
    private const string PrincipalId = "root";
    private const long FixtureTransactions = 2;
    private const string MissingAuthority = "The genuine source lifetime authority is absent.";

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        var partition = ControlledPartitionMovementCorpus.Partition;
        var name = ControlledPartitionMovementCorpus.Collection;
        var key = ResourceLifetimeCatalog.Key(partition.TenantId, partition.DatabaseId, name);
        var sourceBytes = state.Source.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException(MissingAuthority);
        var genuine = state.Proof.ControlProof.Coverage.Lifetimes.Single(value => value.ResourceName == name);
        await ResourceLifetimeGrantAssertions.EqualAsync(genuine, NativeSerialization.Deserialize<ResourceLifetimeV1>(sourceBytes));
        await Assert.That(state.Target.Store.Read(view => view.ReadOwnedValue(key))).IsNull();
        await Assert.That(state.Target.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(partition.TenantId, partition.DatabaseId, name)))).IsNull();
        var sourceBefore = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var targetBefore = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        state.Target.Store.Commit((transaction, _) => { transaction.Put(key, sourceBytes); return true; });
        await Assert.That(state.Target.Store.Read(view => view.ReadOwnedValue(key))!.SequenceEqual(sourceBytes)).IsTrue();
        var injected = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var parent = Work(state.Source, token);
            var frame = state.Source.Database.TryCaptureControlledDocumentReadV2(PrincipalId, Reference(), null,
                Expiry(state.Source), parent) ?? throw new InvalidOperationException(MissingAuthority);
            RefuseMissingCoverage(state, frame, token);
            await HealthyAsync(state, frame, parent, token);
        }, failures);
        await ServerFailureObserver.ObserveAsync(() => sourceBefore.AssertUnchangedAsync(state.Source), failures);
        await ServerFailureObserver.ObserveAsync(() => injected.AssertUnchangedAsync(state.Target), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        state.Target.Store.Commit((transaction, _) => { transaction.Delete(key); return true; });
        await Assert.That(state.Target.Store.Read(view => view.ReadOwnedValue(key))).IsNull();
        await Assert.That(ControlledPartitionMovementRawImage.Bytes(state.Target.Store)
            .SequenceEqual(targetBefore.Image, StringComparer.Ordinal)).IsTrue();
        await Assert.That(state.Target.Store.Position).IsEqualTo(checked(targetBefore.Position + FixtureTransactions));
        await Assert.That(state.Target.Journal.Log.State.LastIndex).IsEqualTo(targetBefore.LastIndex);
        await Assert.That(state.Target.Journal.Log.State.CommittedIndex).IsEqualTo(targetBefore.CommittedIndex);
        await Assert.That(state.Target.Database.LastApplied).IsEqualTo(targetBefore.Applied);
        await sourceBefore.AssertUnchangedAsync(state.Source);
    }

    private static void RefuseMissingCoverage(ResourceLifetimeGrantState state,
        PartitionControlDocumentReadFrameV2 original, CancellationToken token)
    {
        var withoutCoverage = original with
        {
            Coverage = null!
        };
        var receiver = Work(state.Target, token);
        var grant = receiver.CreateReadGrant(state.Target.Database.Limits.MaxBatchBytes,
            state.Target.Database.Limits.MaxBatchMutations);
        var refusal = Assert.ThrowsExactly<KeyLoadException>(() => state.Target.Database.ReadControlledDocumentV2(
            PrincipalId, withoutCoverage, receiver, grant, token));
        if (refusal.Code != ErrorCode.Corruption)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(refusal).Throw();
        }
    }

    private static async Task HealthyAsync(ResourceLifetimeGrantState state,
        PartitionControlDocumentReadFrameV2 original, ReadExecutionBudget parent, CancellationToken token)
    {
        var receiver = Work(state.Target, token);
        var grant = receiver.CreateReadGrant(state.Target.Database.Limits.MaxBatchBytes,
            state.Target.Database.Limits.MaxBatchMutations);
        var actual = state.Target.Database.ReadControlledDocumentV2(PrincipalId, original, receiver, grant, token);
        state.Source.Database.ValidateControlledDocumentReadV2(original, parent);
        await ResourceLifetimeGrantAssertions.EqualAsync(new DocumentResult(Reference(),
            ControlledPartitionMovementCorpus.InitialRevision, ControlledPartitionMovementCorpus.OriginalJson, false, []), actual);
    }

    private static EntityRef Reference() => new(ControlledPartitionMovementCorpus.Partition,
        ControlledPartitionMovementCorpus.Collection, ControlledPartitionMovementCorpus.DocumentId);

    private static DateTimeOffset Expiry(ControlledPartitionMovementNode owner)
        => owner.Database.EvaluationClock.GetUtcNow() + TimeSpan.FromSeconds(owner.Database.Limits.QueryDeadlineSeconds);

    private static ReadExecutionBudget Work(ControlledPartitionMovementNode owner, CancellationToken token)
        => new(Options.Create(owner.Database.Limits), owner.Database.EvaluationClock, token);
}
