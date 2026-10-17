using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoNativeReadinessRegression
{
    private const char LineSeparator = '\n';

    /// <summary>AC-MR-031-004/005: actual same-image BSON domains and native priority election; shared selector owns invocation.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, string password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, MongoNativeReadinessRegressionProtocol.MinimumNodes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, MongoNativeReadinessRegressionProtocol.MaximumNodes);
        using var parentTimeout = new CancellationTokenSource(MongoNativeReadinessRegressionProtocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, parentTimeout.Token);
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>().ToArray();
        var topology = await MongoNativeReadinessRegressionTopology.ReadAsync(resources, nodeCount, parent.Token);
        var child = MongoNativeReadinessRegressionChild.Create(topology.Image, topology.Network, topology.Module, password, nodeCount);
        var moduleHash = await HashAsync(child.Module, parent.Token);
        var fixtureHash = await HashAsync(child.Script, parent.Token);
        var output = await RunChildAsync(child, parent.Token);
        await Assert.That(await HashAsync(child.Module, parent.Token)).IsEqualTo(moduleHash);
        await Assert.That(await HashAsync(child.Script, parent.Token)).IsEqualTo(fixtureHash);
        var lines = output.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Assert.That(lines.Count(line => line == MongoNativeReadinessRegressionProtocol.DomainPassed)).IsEqualTo(MongoNativeReadinessRegressionProtocol.ExpectedMarkerCount);
        await Assert.That(lines.Count(line => line == MongoNativeReadinessRegressionProtocol.AdmissionPassed)).IsEqualTo(MongoNativeReadinessRegressionProtocol.ExpectedMarkerCount);
        if (nodeCount == MongoNativeReadinessRegressionProtocol.MaximumNodes)
        {
            await Assert.That(lines.Count(line => line == MongoNativeReadinessRegressionProtocol.LowerObserved)).IsEqualTo(MongoNativeReadinessRegressionProtocol.ExpectedMarkerCount);
            await Assert.That(lines.Count(line => line == MongoNativeReadinessRegressionProtocol.ElectionPassed)).IsEqualTo(MongoNativeReadinessRegressionProtocol.ExpectedMarkerCount);
        }
    }

    private static async Task<string> RunChildAsync(MongoNativeReadinessRegressionChild child, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new MongoNativeReadinessRegressionProcess(child.Arguments(), child.Environment);
        var original = process.CompleteAsync(token);
        try
        {
            await original;
        }
        finally
        {
            await MongoNativeReadinessRegressionProcess.JoinAsync(original, CleanupOwnedAsync(process, child));
        }
        return await original;
    }

    private static async Task CleanupOwnedAsync(MongoNativeReadinessRegressionProcess process, MongoNativeReadinessRegressionChild child)
    {
        using var cleanup = new CancellationTokenSource(MongoNativeReadinessRegressionProtocol.CleanupDeadline, TimeProvider.System);
        await CleanupAsync(process, child, cleanup.Token);
    }

    private static async Task CleanupAsync(MongoNativeReadinessRegressionProcess process,
        MongoNativeReadinessRegressionChild child, CancellationToken token)
    {
        var client = process.StopClientAsync(token);
        var childCleanup = MongoNativeReadinessRegressionDocker.RemoveOwnedAsync(child, token);
        var readers = process.DrainAsync(token);
        var originals = MongoNativeReadinessRegressionProcess.JoinAsync(client, childCleanup, readers);
        try
        {
            await originals;
        }
        finally
        {
            await MongoNativeReadinessRegressionProcess.JoinAsync(originals, process.ReleaseAsync(token));
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var source = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(source, token));
    }
}
