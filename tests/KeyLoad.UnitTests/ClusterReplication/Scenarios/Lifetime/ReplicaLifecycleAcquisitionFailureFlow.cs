using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Replication;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.IO;
using A = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaLifecycleAcquisitionAssertions;
using P = KeyLoad.UnitTests.Features.ClusterReplication.ReplicaBarrierDeadlineScenario;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class ReplicaLifecycleAcquisitionFailureFlow
{
    private const string Prefix = "keyload-replica-acquisition-";
    private const string GuidFormat = "N";
    private const string CanonicalDirectory = "canonical";
    private const string ReplicaDirectory = "replica";
    private const string Document = "acquisition-original";
    private const string PutKind = "putDocument";
    internal const string FirstJson = "{\"value\":1}";
    private const string SecondJson = "{\"value\":2}";
    internal const long FirstRevision = 1;
    private const long SecondRevision = 2;
    private const long FirstIndex = 1;
    private const long OriginalIndex = 2;
    private const long OriginalTerm = 1;
    private const long PlacementEpoch = 1;
    private const string MissingSeed = "The original native acquisition seed did not complete.";
    private const long InitialRevision = 0;
    private const int NoFailures = 0;
    private const int InvalidApplyBatch = 0;

    internal static async Task RunAsync(bool replicaLocked, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), Prefix + Guid.NewGuid().ToString(GuidFormat));
        var failures = new List<Exception>();
        using var deadline = new CancellationTokenSource(ReplicaExecutionTestOptions.Execution().Value.CommandTimeout, TimeProvider.System);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        await ServerFailureObserver.ObserveAsync(() => ExerciseAsync(root, replicaLocked, failures, linked.Token), failures);
        if (failures.Count == NoFailures)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExerciseAsync(string root, bool replicaLocked, List<Exception> failures, CancellationToken token)
    {
        Directory.CreateDirectory(root);
        ReplicaLifecycleAcquisitionState? before = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var fixture = new ReplicaLifecycleFixture(borrowedDirectory: root);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                before = await SeedAsync(fixture, token);
            }, failures);
        }, failures);
        if (failures.Count != NoFailures)
        {
            return;
        }
        var original = before ?? throw new InvalidOperationException(MissingSeed);
        await RefuseAsync(root, replicaLocked, original, failures);
        if (failures.Count != NoFailures)
        {
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var reopened = new ReplicaLifecycleFixture(configuration: original.Configuration, borrowedDirectory: root);
            await ServerFailureObserver.ObserveAsync(() => HealthyAsync(reopened, original, token), failures);
        }, failures);
    }

    private static async Task<ReplicaLifecycleAcquisitionState> SeedAsync(ReplicaLifecycleFixture fixture, CancellationToken token)
    {
        var creation = new ReplicaBarrierDeadlineScenario().Prepare(fixture.Database, fixture.Configuration);
        var definition = new ResourceDefinition(P.Collection, ResourceKind.Collection, P.Partition.TransactionDomainId);
        var lifetime = ResourceLifetimeIdentity.Create(creation, P.Partition.TenantId, P.Partition.DatabaseId, definition);
        var reference = new EntityRef(P.Partition, P.Collection, Document)
        {
            Lifetime = new(lifetime.Incarnation.Value)
        };
        fixture.Log.SaveTermAndVote(OriginalTerm, fixture.Configuration.LocalId);
        fixture.Log.Append([new(FirstIndex, OriginalTerm, null)]);
        fixture.Materializer.Commit(FirstIndex);
        await fixture.Materializer.WaitForApplyAsync(FirstIndex, token);
        var operation = await ApplyAsync(fixture, FirstJson, InitialRevision, OriginalIndex, token);
        var receipt = Expected(fixture, operation.Id, OriginalIndex, FirstRevision);
        await A.ReceiptAsync(receipt, fixture.Database.ResolveSubmissionOutcome(operation).Get<CommitReceipt>());
        await A.DocumentAsync(fixture, reference, FirstRevision, FirstJson, operation.EvaluatedAt, token);
        return new(fixture.Configuration, operation, receipt, reference, A.Capture((ZoneTreeStore)fixture.Database.Store),
            A.Capture(fixture.ReplicaStore), NativeSerialization.Serialize(fixture.Log.State), fixture.Database.LastApplied);
    }

    private static async Task RefuseAsync(string root, bool replicaLocked, ReplicaLifecycleAcquisitionState before,
        List<Exception> failures)
    {
        if (!replicaLocked)
        {
            var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            {
                await using var unexpected = new ReplicaLifecycleFixture(executionSettings: new()
                {
                    ApplyBatchSize = InvalidApplyBatch
                },
                    configuration: before.Configuration, borrowedDirectory: root);
            });
            await Assert.That(error!.Message).IsEqualTo(ReplicaProtocol.InvalidLimits);
            return;
        }
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var occupied = Open(root, ReplicaDirectory, before.Configuration.Incarnation);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var error = await Assert.ThrowsExactlyAsync<IOException>(async () =>
                {
                    await using var unexpected = new ReplicaLifecycleFixture(configuration: before.Configuration, borrowedDirectory: root);
                });
                await Assert.That(OfflineNativeErrors.IsLockContention(error!)).IsTrue();
                await ProbeCanonicalAsync(root, before, failures);
                await A.StoreAsync(occupied, before.Replica);
            }, failures);
        }, failures);
    }

    private static async Task ProbeCanonicalAsync(string root, ReplicaLifecycleAcquisitionState before, List<Exception> failures)
    {
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var canonical = Open(root, CanonicalDirectory, before.Configuration.Incarnation);
            await ServerFailureObserver.ObserveAsync(() => A.StoreAsync(canonical, before.Canonical), failures);
        }, failures);
    }

    private static ZoneTreeStore Open(string root, string name, Guid incarnation)
        => new(new(Path.Combine(root, name))
        {
            Incarnation = incarnation
        },
            UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());

    private static async Task HealthyAsync(ReplicaLifecycleFixture fixture, ReplicaLifecycleAcquisitionState before, CancellationToken token)
    {
        await A.OriginalAsync(fixture, before, token);
        var index = before.Receipt.Token.Position + FirstIndex;
        var operation = await ApplyAsync(fixture, SecondJson, FirstRevision, index, token);
        await A.ReceiptAsync(Expected(fixture, operation.Id, index, SecondRevision),
            fixture.Database.ResolveSubmissionOutcome(operation).Get<CommitReceipt>());
        await A.DocumentAsync(fixture, before.Reference, SecondRevision, SecondJson, operation.EvaluatedAt, token);
        var canonical = A.Capture((ZoneTreeStore)fixture.Database.Store);
        var replica = A.Capture(fixture.ReplicaStore);
        await A.ReceiptAsync(before.Receipt, fixture.Database.ResolveSubmissionOutcome(before.Original).Get<CommitReceipt>());
        await A.StoreAsync((ZoneTreeStore)fixture.Database.Store, canonical);
        await A.StoreAsync(fixture.ReplicaStore, replica);
    }

    private static async Task<ReplicatedOperation> ApplyAsync(ReplicaLifecycleFixture fixture, string json, long revision,
        long index, CancellationToken token)
    {
        var request = new CommandRequest(Guid.NewGuid(), P.Partition, [new PutDocument(P.Collection, Document, json, revision)]);
        var work = new ReadExecutionBudget(fixture.Database.OperationLimitsOptions, fixture.Database.EvaluationClock, token);
        var original = fixture.Database.CreateNativeOperation(OperationKind.Batch, request.CommandId, P.Root,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request), work);
        fixture.Log.Append([new(index, OriginalTerm, original)]);
        fixture.Materializer.Commit(index);
        await fixture.Materializer.WaitForApplyAsync(index, token);
        return original;
    }

    private static CommitReceipt Expected(ReplicaLifecycleFixture fixture, Guid id, long index, long revision)
        => new(id, new(fixture.Configuration.Incarnation, P.Partition.AtomicPartitionId, index, PlacementEpoch),
            [new(PutKind, P.Collection, Document, revision)], DurabilityProfile.ProcessDurable);
}
