using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsAnnRetirementFlow
{
    private const string NextConsumerSuffix = "-next";
    private const long NextGenerationStep = 1;

    internal static async Task RunAsync(RequestCqrsHostedRuntimeFixture fixture,
        AnnMaintenanceRequest original, string originalLeaf, CancellationToken token)
    {
        var root = Path.Combine(fixture.Database.Directory, NativeAnnProtocol.RootDirectory);
        var receipt = NativeAnnReceiptFiles.Read<NativeAnnRootReceipt>(Path.Combine(root,
            NativeAnnProtocol.RootReceipt), new NativeAnnExecutionOptions()).Value;
        var key = new NativeAnnOwnedKey(original.Consumer, original.IndexGeneration);
        await Assert.That(receipt.Published.Any(row => row.Key == key)).IsFalse();
        await Assert.That(receipt.Pending.Any(row => row.Key == key)).IsFalse();
        await Assert.That(receipt.OriginalOperations.Any(row => row.Key == key)).IsFalse();
        await Assert.That(Directory.Exists(Path.Combine(root, originalLeaf))).IsFalse();
        var canonical = fixture.Database.Database.GetOutboxStatus(RequestCqrsAnnTestData.Principal,
            original.Consumer.Partition).Consumers.Single(row => row.Consumer == original.Consumer);
        await Assert.That(canonical.Released).IsTrue();
        await Assert.That(canonical.InitialAnnMaintenanceSettled).IsTrue();
        await RequestCqrsAnnAssertions.EqualAsync(original, canonical.InitialAnnMaintenance);
        var next = original with
        {
            CommandId = Guid.NewGuid(),
            Consumer = new(original.Consumer.Partition, original.Consumer.Name + NextConsumerSuffix),
            IndexGeneration = checked(original.IndexGeneration + NextGenerationStep)
        };
        var before = fixture.Database.Store.Position;
        var built = await RequestCqrsAnnInvocation.RunAsync(fixture, next, token);
        await RequestCqrsAnnResultAssertions.RequireAsync(fixture, next, built.Result, before, changed: true, token);
        await RequestCqrsAnnOriginalReplay.RequireAsync(fixture, next, built.Result, token);
        var release = next with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Release
        };
        var released = await RequestCqrsAnnInvocation.RunAsync(fixture, release, token);
        await RequestCqrsAnnAssertions.EqualAsync(new AnnMaintenanceResult(release.CommandId, release.Consumer,
            release.IndexGeneration, AnnMaintenancePhase.Completed, null, RequestCqrsAnnTestData.EmptyCount,
            null, null), released.Result);
    }
}
