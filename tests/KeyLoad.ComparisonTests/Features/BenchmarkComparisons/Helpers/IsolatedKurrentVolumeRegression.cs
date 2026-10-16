using System.Runtime.ExceptionServices;
using Aspire.Hosting;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class IsolatedKurrentVolumeRegression
{
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, CancellationToken token)
    {
        var harnessOptions = NativeExecutionPolicyFixture.Harness();
        using var deadlineTimeout = new CancellationTokenSource(harnessOptions.Value.KurrentVolumeTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, deadlineTimeout.Token);
        var options = new ComparisonOptions
        {
            Documents = 4_096,
            Repetitions = 5,
            Warmup = 256,
            Operations = 10_000,
            Concurrency = 16,
            Topology = new BenchmarkSessionSelection(KurrentConstants.Name, nodeCount).Topology
        };
        await IsolatedKurrentVolumeRegressionNative.RequireCanonicalProfileAsync(options);
        var endpoint = await IsolatedKurrentCleanupRegressionGossip.ReadLeaderAsync(app, nodeCount, deadline.Token);
        var fixture = new IsolatedKurrentVolumeRegressionFixture(endpoint, options, harnessOptions);
        ExceptionDispatchInfo? primary = null;
        try
        {
            await fixture.SeedAsync(deadline.Token);
            await fixture.CleanupAndVerifyAsync(deadline.Token);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
            throw;
        }
        finally
        {
            await fixture.CleanupRemainingAsync(primary);
        }
    }
}
