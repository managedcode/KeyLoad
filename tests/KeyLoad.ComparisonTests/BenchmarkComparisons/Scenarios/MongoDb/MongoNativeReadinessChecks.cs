using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class MongoNativeReadinessChecks
{
    private const char LineSeparator = '\n';

    /// <summary>AC-MR-031-004/005: actual same-image BSON domains and native priority election; shared selector owns invocation.</summary>
    internal static async Task VerifyAsync(DistributedApplication app, int nodeCount, string password, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeCount, MongoNativeReadinessChecksProtocol.MinimumNodes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(nodeCount, MongoNativeReadinessChecksProtocol.MaximumNodes);
        using var parentTimeout = new CancellationTokenSource(MongoNativeReadinessChecksProtocol.ParentDeadline, TimeProvider.System);
        using var parent = CancellationTokenSource.CreateLinkedTokenSource(token, parentTimeout.Token);
        var resources = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>().ToArray();
        var topology = await MongoNativeReadinessChecksTopology.ReadAsync(resources, nodeCount, parent.Token);
        var child = MongoNativeReadinessChecksChild.Create(topology.Image, topology.Network, topology.Module, password, nodeCount);
        var moduleHash = await HashAsync(child.Module, parent.Token);
        var fixtureHash = await HashAsync(child.Script, parent.Token);
        var output = await RunChildAsync(child, parent.Token);
        await Assert.That(await HashAsync(child.Module, parent.Token)).IsEqualTo(moduleHash);
        await Assert.That(await HashAsync(child.Script, parent.Token)).IsEqualTo(fixtureHash);
        var lines = output.Split(LineSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        await Assert.That(lines.Count(line => line == MongoNativeReadinessChecksProtocol.DomainPassed)).IsEqualTo(MongoNativeReadinessChecksProtocol.ExpectedMarkerCount);
        await Assert.That(lines.Count(line => line == MongoNativeReadinessChecksProtocol.AdmissionPassed)).IsEqualTo(MongoNativeReadinessChecksProtocol.ExpectedMarkerCount);
        if (nodeCount == MongoNativeReadinessChecksProtocol.MaximumNodes)
        {
            await Assert.That(lines.Count(line => line == MongoNativeReadinessChecksProtocol.LowerObserved)).IsEqualTo(MongoNativeReadinessChecksProtocol.ExpectedMarkerCount);
            await Assert.That(lines.Count(line => line == MongoNativeReadinessChecksProtocol.ElectionPassed)).IsEqualTo(MongoNativeReadinessChecksProtocol.ExpectedMarkerCount);
        }
    }

    private static async Task<string> RunChildAsync(MongoNativeReadinessChecksChild child, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = new MongoNativeReadinessChecksProcess(child.Arguments(), child.Environment);
        var original = process.CompleteAsync(token);
        try
        {
            await original;
        }
        finally
        {
            await MongoNativeReadinessChecksProcess.JoinAsync(original, CleanupOwnedAsync(process, child));
        }
        return await original;
    }

    private static async Task CleanupOwnedAsync(MongoNativeReadinessChecksProcess process, MongoNativeReadinessChecksChild child)
    {
        using var cleanup = new CancellationTokenSource(MongoNativeReadinessChecksProtocol.CleanupDeadline, TimeProvider.System);
        await CleanupAsync(process, child, cleanup.Token);
    }

    private static async Task CleanupAsync(MongoNativeReadinessChecksProcess process,
        MongoNativeReadinessChecksChild child, CancellationToken token)
    {
        var client = process.StopClientAsync(token);
        var childCleanup = MongoNativeReadinessChecksDocker.RemoveOwnedAsync(child, token);
        var readers = process.DrainAsync(token);
        var originals = MongoNativeReadinessChecksProcess.JoinAsync(client, childCleanup, readers);
        try
        {
            await originals;
        }
        finally
        {
            await MongoNativeReadinessChecksProcess.JoinAsync(originals, process.ReleaseAsync(token));
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var source = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(source, token));
    }
}
