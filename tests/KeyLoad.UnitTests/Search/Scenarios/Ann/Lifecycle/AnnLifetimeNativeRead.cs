using KeyLoad.Core.Features.ClusterRouting.Queries;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal static class AnnLifetimeNativeRead
{
    internal static AnnReadLifetimeMetadata Capture(TestDatabase source)
        => source.Store.Read<AnnReadLifetimeMetadata>(view =>
        {
            var work = source.CreateReadWork(TestContext.Current!.Execution.CancellationToken);
            var bounded = work.CreateView(view);
            var principal = source.Database.Principal(bounded, AnnLifetimeNativeProtocol.Reader, source.Database.EvaluationClock.GetUtcNow());
            var definition = source.Database.Resource(bounded, source.Partition, AnnSeedTestSupport.Collection, ResourceKind.Collection);
            var definitions = PartitionResourceCatalog.Capture(bounded, source.Partition, source.Database.Limits);
            var coverage = ResourceLifetimeCoverage.Capture(bounded, source.Partition, definitions, source.Database.Limits.MaxBatchBytes);
            return new(definition, coverage, principal, definitions);
        });

    internal static ApproximateSearchRequest Request(TestDatabase target, AnnMaintenanceRequest pin)
        => AnnPublicWholeFlow.Request(target, pin) with
        {
            Search = AnnPublicWholeFlow.Request(target, pin).Search with
            {
                Limit = AnnLifetimeNativeProtocol.Count
            }
        };

    internal static Task<AnnSearchPage> SearchAsync(TestDatabase source, TestDatabase target,
        NativeAnnMaintenanceTestRuntime runtime, ApproximateSearchRequest request)
        => AnnPublicWholeFlow.Engine(target, runtime).ApproximateSearchAsync(AnnLifetimeNativeProtocol.Reader,
            request, target.CreateReadWork(TestContext.Current!.Execution.CancellationToken), Capture(source),
            TestContext.Current!.Execution.CancellationToken);

    internal static async Task WaitAsync(TestDatabase source, TestDatabase target, NativeAnnMaintenanceTestRuntime runtime,
        AnnMaintenanceRequest pin, CommitReceipt receipt)
    {
        var result = await AnnPublicWholeFlow.Engine(target, runtime).WaitForAnnIndexAsync(AnnLifetimeNativeProtocol.Reader,
            new(target.Partition, AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(),
                pin.Consumer, pin.IndexGeneration, receipt.Token), target.CreateReadWork(TestContext.Current!.Execution.CancellationToken),
            Capture(source), TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.SchemaVersion).IsEqualTo(AnnLifetimeNativeProtocol.TargetSchema);
        await Assert.That(result.PolicyEpoch).IsEqualTo(AnnLifetimeNativeProtocol.TargetEpoch);
        await Assert.That(result.IndexedToken.Position >= receipt.Token.Position).IsTrue();
    }
}
