using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class MetadataBootstrapRf3Cold(ClusterFixture fixture)
{
    private const long MaximumReceiptBytes = 65_536;
    private readonly List<Evidence> originals = [];
    internal string Claim(IEnumerable<string> nodes)
    {
        var scenario = ModelSqlRf3ColdOwnership.Require(fixture).Claim(fixture);
        foreach (var node in nodes)
        {
            if (File.Exists(PathFor(scenario, node)))
            {
                throw new InvalidOperationException("A unique original metadata owner receipt already exists.");
            }
        }
        return scenario;
    }
    internal async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(token), failures);
        var scenario = Claim(MetadataBootstrapRf3State.Nodes);
        var stopped = await Task.WhenAll(MetadataBootstrapRf3State.Nodes.Select(node => ObserveAsync(
            () => fixture.KillContainerAsync(node, scenario, token))));
        var started = await Task.WhenAll(MetadataBootstrapRf3State.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartContainerAsync(node, token))));
        failures.AddRange(stopped.SelectMany(value => value));
        failures.AddRange(started.SelectMany(value => value));
        foreach (var node in MetadataBootstrapRf3State.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => CaptureAsync(scenario, MetadataBootstrapRf3State.Nodes, token), failures);
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task CaptureAsync(string scenario, IEnumerable<string> nodes, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var path = PathFor(scenario, node);
                await using var stream = File.OpenRead(path);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    if (stream.Length is <= 0 or > MaximumReceiptBytes)
                    {
                        throw new InvalidOperationException("The original metadata owner receipt exceeds its bound.");
                    }
                    var length = stream.Length;
                    originals.Add(new(path, length, await DigestAsync(stream, length, token)));
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task RequireAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var input = File.OpenRead(original.Path);
                await Assert.That(input.Length).IsEqualTo(original.Length);
                await Assert.That((await DigestAsync(input, original.Length, token)).AsSpan().SequenceEqual(original.Hash)).IsTrue();
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<byte[]> DigestAsync(FileStream input, long length, CancellationToken token)
    {
        if (length is <= 0 or > MaximumReceiptBytes || input.Length != length)
        {
            throw new InvalidOperationException("The original metadata owner receipt changed length.");
        }
        var bytes = new byte[checked((int)length)];
        await input.ReadExactlyAsync(bytes, token);
        if (input.Length != length)
        {
            throw new InvalidOperationException("The original metadata owner receipt changed while being read.");
        }
        return SHA256.HashData(bytes);
    }
    private static string PathFor(string scenario, string node) => ContainerRuntimeOwnedReceipt.PathFor(
        ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, scenario, node);
    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
    private sealed record Evidence(string Path, long Length, byte[] Hash);
}
