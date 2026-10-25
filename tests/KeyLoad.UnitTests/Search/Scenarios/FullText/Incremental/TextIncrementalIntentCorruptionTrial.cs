using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

/// <summary>Owns the real prepared page, joined owner, damaged envelope and exact original repair lifetime.</summary>
internal static class TextIncrementalIntentCorruptionTrial
{
    private const byte DamageMask = 0xFF;
    private const string CorruptionDetail = "The execution text projection is corrupt.";

    internal static async Task RunAsync(TestDatabase database, CancellationToken token)
    {
        database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
        _ = await TextMaintenanceSeed.CommitAsync(database, token);
        var request = await TextMaintenanceRequestFixture.CreateAsync(database, token);
        var original = await PrepareAsync(database, request, token);
        var options = UnitTextOptions.Execution();
        var root = Path.Combine(database.Directory, TextIncrementalProtocol.RootDirectory);
        var budget = new ReadExecutionBudget(cancellationToken: token);
        var leaf = TextIncrementalEnrollmentFiles.Find(root, request, budget, options)
            ?? throw new InvalidOperationException();
        var path = Path.Combine(root, leaf, TextIncrementalProtocol.IntentFile);
        var bytes = await File.ReadAllBytesAsync(path, token);
        var damaged = bytes.ToArray();
        damaged[^1] ^= DamageMask;
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failures = new List<Exception>();
        await File.WriteAllBytesAsync(path, damaged, token);
        try
        {
            await ServerFailureObserver.ObserveAsync(() => DeniedAsync(database, request, path, damaged, image, position, token), failures);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(path, bytes, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await TextIncrementalIntentContinuation.RunAsync(database, request, original, token);
    }

    private static async Task<CommitProjectionBatchRequest> PrepareAsync(TestDatabase database,
        TextIndexMaintenanceRequest request, CancellationToken token)
    {
        CommitProjectionBatchRequest? original = null;
        await TextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
            var page = database.Database.ReadProjectionBatch(TextMaintenanceTestValues.Principal,
                new(request.Consumer, ThroughSequence: begin.ReplayUpperSequence));
            original = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
            var prepared = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.PreparePage,
                page, original, token: token);
            await Assert.That(OrleansSerialization.Serialize(prepared.OriginalCheckpointIntent!).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        });
        return original ?? throw new InvalidOperationException();
    }

    private static async Task DeniedAsync(TestDatabase database, TextIndexMaintenanceRequest request,
        string path, byte[] damaged, string[] image, long position, CancellationToken token)
    {
        await TextIncrementalIntentOwner.RunAsync(database, async runtime =>
        {
            TextMaintenanceCapabilityResult? partial = null;
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(async () => partial =
                await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token))
                ?? throw new InvalidOperationException();
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
            await Assert.That(partial).IsNull();
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
            await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(damaged)).IsTrue();
        });
    }
}
