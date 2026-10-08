using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeAnnPendingIntegrityTests
{
    private const int Count = 3;
    private const int One = 1;
    private const int Ambiguous = 2;
    private const string Pattern = "generation-*";

    [Test]
    public async Task ActualTruncatedPendingArraysCannotLoadOrPublishAndOriginalBytesResumeHealthy()
    {
        using var database = AnnSeedTestSupport.Create(Count);
        var request = NativeAnnMaintenanceTestData.Pin(database);
        var upper = await PreparePendingAsync(database, request);
        var paths = Directory.EnumerateDirectories(Path.Combine(database.Directory, NativeAnnProtocol.RootDirectory), Pattern)
            .Take(Ambiguous).ToArray();
        await Assert.That(paths.Length).IsEqualTo(One);
        var file = Path.Combine(paths.Single(), NativeAnnProtocol.IndexFile);
        var token = TestContext.Current!.Execution.CancellationToken;
        var original = await File.ReadAllBytesAsync(file, token);
        await File.WriteAllBytesAsync(file, original.AsSpan(0, original.Length - One).ToArray(), token);
        await RejectLoadAsync(database, request with { Mode = AnnMaintenanceMode.Restore });
        await File.WriteAllBytesAsync(file, original, token);
        var restore = request with { Mode = AnnMaintenanceMode.Restore };
        await NativeAnnPendingIntegrityAssertions.ResumeAsync(database, restore, upper);
    }

    private static async Task<long> PreparePendingAsync(TestDatabase database, AnnMaintenanceRequest request)
    {
        var failures = new List<Exception>();
        long upper = 0;
        try
        {
            await using var runtime = new NativeAnnMaintenanceTestRuntime(database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                upper = began.Source!.ThroughSequence;
                var page = NativeAnnMaintenanceTestData.Read(database, request, upper);
                var intent = NativeAnnMaintenanceTestData.Intent(page);
                _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
                _ = await NativeAnnMaintenancePhaseAssertions.UnchangedPhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
                _ = await NativeAnnMaintenancePhaseAssertions.CommitReplayAsync(database, intent);
            }, failures);
        }
        catch (Exception cleanup) when (KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        catch (Exception cleanup) when (!KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(cleanup)) { failures.Add(cleanup); }
        ServerFailureObserver.ThrowIfAny(failures);
        return upper;
    }

    private static Task RejectLoadAsync(TestDatabase database, AnnMaintenanceRequest request)
        => NativeAnnPendingIntegrityAssertions.RejectAsync(database, request);
}
