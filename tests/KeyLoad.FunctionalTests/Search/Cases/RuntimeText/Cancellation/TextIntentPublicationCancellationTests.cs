using KeyLoad.Clustering;
using KeyLoad.Server.Search;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextIntentPublicationCancellationTests
{
    [Test]
    public async Task CancellationAfterOwnedRenameRemovesUnpublishedIntentThenPersistsExactHealthyIntent()
    {
        using var database = new TestDatabase();
        var (intent, root) = await PrepareOriginalAsync(database);
        var options = UnitTextOptions.Execution();
        var generation = Path.Combine(root, "owned-intent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(generation);
        var current = Path.Combine(generation, TextIncrementalProtocol.IntentFile);
        var pending = Path.Combine(generation, TextIncrementalProtocol.PendingIntentFile);
        var sentinel = Path.Combine(generation, "original-owner.bin");
        const string original = "The original owned file must survive canceled preparation.";
        await File.WriteAllTextAsync(sentinel, original, TestContext.Current!.Execution.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var resources = new TextResourceOwnership(options);
        resources.RegisterRoot(root, budget =>
        {
            budget?.Check();
            TextRootFiles.VerifyReceipt(root, database.Store.Identity.NodeId, options);
            if (File.Exists(current))
            {
                cancellation.Cancel();
            }
        });
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => TextIncrementalMetadata.PersistIntent(
            generation, intent, options.Value.MaximumDiskBytes, new ReadExecutionBudget(cancellation.Token), options, resources));
        await Assert.That(error.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(File.Exists(current)).IsFalse();
        await Assert.That(File.Exists(pending)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(sentinel)).IsEqualTo(original);

        TextIncrementalMetadata.PersistIntent(generation, intent, options.Value.MaximumDiskBytes,
            new ReadExecutionBudget(), options, resources);
        var healthy = TextIncrementalMetadata.ReadIntent(generation, options.Value.MaximumDiskBytes, new ReadExecutionBudget());
        await Assert.That(OrleansSerialization.Serialize(healthy).AsSpan().SequenceEqual(OrleansSerialization.Serialize(intent))).IsTrue();
        await Assert.That(File.Exists(pending)).IsFalse();
        await Assert.That(await File.ReadAllTextAsync(sentinel)).IsEqualTo(original);
    }

    private static async Task<(TextIncrementalIntent Intent, string Root)> PrepareOriginalAsync(TestDatabase database)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        database.Configure(TextBilingualAudit.Collection, ResourceKind.Collection);
        _ = await TextMaintenanceSeed.CommitAsync(database, token);
        var request = await TextMaintenanceRequestFixture.CreateAsync(database, token);
        await using var runtime = new TextMaintenanceTestRuntime(database);
        var begin = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.Begin, token: token);
        var page = database.Database.ReadProjectionBatch(TextMaintenanceTestValues.Principal,
            new(request.Consumer, ThroughSequence: begin.ReplayUpperSequence));
        var command = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
        _ = await runtime.PhaseAsync(database, request, TextMaintenanceCapabilityKind.PreparePage, page, command, token: token);
        var path = Directory.EnumerateFiles(database.Directory, TextIncrementalProtocol.IntentFile, SearchOption.AllDirectories).Single();
        var generation = Path.GetDirectoryName(path)!;
        var intent = TextIncrementalMetadata.ReadIntent(generation, UnitTextOptions.Execution().Value.MaximumDiskBytes, new ReadExecutionBudget(token));
        return (intent, Directory.GetParent(generation)!.FullName);
    }
}
