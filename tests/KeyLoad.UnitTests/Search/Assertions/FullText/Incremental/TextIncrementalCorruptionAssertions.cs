using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Search;
using KeyLoad.UnitTests.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Search;

internal static class TextIncrementalCorruptionAssertions
{
    private const byte DamageMask = 0xFF;
    private const int TrackedRecords = 2;
    private const string CorruptionDetail = "The execution text projection is corrupt.";

    internal static async Task VerifyAsync(TestDatabase database, TextIndexMaintenanceRequest build,
        CancellationToken token)
    {
        var options = UnitTextOptions.Execution();
        var root = Path.Combine(database.Directory, TextIncrementalProtocol.RootDirectory);
        var budget = new ReadExecutionBudget(cancellationToken: token);
        var leaf = TextIncrementalEnrollmentFiles.Find(root, build, budget, options)
            ?? throw new InvalidOperationException();
        var path = Path.Combine(root, leaf, TextIncrementalProtocol.ManifestFile);
        var original = await File.ReadAllBytesAsync(path, token);
        var damaged = original.ToArray();
        damaged[^1] ^= DamageMask;
        var restore = build with
        {
            CommandId = Guid.NewGuid(),
            Mode = TextIndexMaintenanceMode.Restore
        };
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var failures = new List<Exception>();
        await File.WriteAllBytesAsync(path, damaged, token);
        try
        {
            await ServerFailureObserver.ObserveAsync(
                () => DamagedAsync(database, restore, path, damaged, image, position, failures, token), failures);
        }
        finally
        {
            await ServerFailureObserver.ObserveAsync(() => File.WriteAllBytesAsync(path, original, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await TextMissingSettledAuthorityFlow.RejectAndRepairAsync(database, restore, path, original, token);
        await ServerFailureObserver.ObserveAsync(() => HealthyAsync(database, restore, failures, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task DamagedAsync(TestDatabase database, TextIndexMaintenanceRequest restore,
        string path, byte[] damaged, string[] image, long position, List<Exception> failures, CancellationToken token)
    {
        await using var runtime = new TextMaintenanceTestRuntime(database);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => runtime.PhaseAsync(database,
                restore, TextMaintenanceCapabilityKind.Begin, token: token)) ?? throw new InvalidOperationException();
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(error.Message).IsEqualTo(CorruptionDetail);
            await Assert.That(database.Store.Position).IsEqualTo(position);
            await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(image, CollectionOrdering.Matching);
            await Assert.That(await File.ReadAllBytesAsync(path, token)).IsEquivalentTo(damaged, CollectionOrdering.Matching);
        }, failures);
    }

    private static async Task HealthyAsync(TestDatabase database, TextIndexMaintenanceRequest restore,
        List<Exception> failures, CancellationToken token)
    {
        await using var healthy = new TextMaintenanceTestRuntime(database);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var completed = await TextMaintenancePhaseFlow.FinishAsync(database, healthy, restore, token);
            await Assert.That(completed.TrackedRecords).IsEqualTo(TrackedRecords);
            await Assert.That(completed.Checkpoint).IsEqualTo(completed.ThroughSequence);
            await TextMissingSettledAuthorityFlow.HealthyAsync(database, healthy, restore, token);
        }, failures);
    }
}
