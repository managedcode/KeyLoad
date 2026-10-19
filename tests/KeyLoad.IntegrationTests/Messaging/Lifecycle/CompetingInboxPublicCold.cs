using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class CompetingInboxPublicCold(ClusterFixture fixture)
{
    private const string OriginalReceiptReadLengthDetail = "The original competing inbox owner receipt changed while being read.";
    private const string OriginalReceiptLengthDetail = "The original competing inbox owner receipt changed length.";
    private const string OriginalReceiptBoundDetail = "The original competing inbox owner receipt exceeds its bound.";
    private const string OriginalReceiptExistsDetail = "A unique original competing inbox owner receipt already exists.";
    private const long MaximumReceiptBytes = 65_536;
    private readonly List<Evidence> originals = [];
    internal string Claim(IEnumerable<string> nodes)
    {
        var scenario = ModelSqlClusterColdOwnership.Require(fixture).Claim(fixture);
        foreach (var node in nodes)
        {
            if (File.Exists(PathFor(scenario, node)))
            {
                throw new InvalidOperationException(OriginalReceiptExistsDetail);
            }
        }
        return scenario;
    }
    internal async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(token), failures);
        var scenario = Claim(CompetingInboxPublicProtocol.Nodes);
        var stopped = await Task.WhenAll(CompetingInboxPublicProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.KillServerAsync(node, scenario, token))));
        var started = await Task.WhenAll(CompetingInboxPublicProtocol.Nodes.Select(node => ObserveAsync(
            () => fixture.RestartServerAsync(node, token))));
        failures.AddRange(stopped.SelectMany(value => value));
        failures.AddRange(started.SelectMany(value => value));
        foreach (var node in CompetingInboxPublicProtocol.Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.App.ResourceNotifications.WaitForResourceHealthyAsync(
                node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => CaptureAsync(scenario, CompetingInboxPublicProtocol.Nodes, token), failures);
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
                        throw new InvalidOperationException(OriginalReceiptBoundDetail);
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
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(input.Length).IsEqualTo(original.Length);
                    await Assert.That((await DigestAsync(input, original.Length, token)).AsSpan().SequenceEqual(original.Hash)).IsTrue();
                }, failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<byte[]> DigestAsync(FileStream input, long length, CancellationToken token)
    {
        if (length is <= 0 or > MaximumReceiptBytes || input.Length != length)
        {
            throw new InvalidOperationException(OriginalReceiptLengthDetail);
        }
        var bytes = new byte[checked((int)length)];
        await input.ReadExactlyAsync(bytes, token);
        if (input.Length != length)
        {
            throw new InvalidOperationException(OriginalReceiptReadLengthDetail);
        }
        return SHA256.HashData(bytes);
    }
    private static string PathFor(string scenario, string node) => ProcessRuntimeOwnedReceipt.PathFor(
        ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, scenario, node);
    private static async Task<Exception[]> ObserveAsync(Func<Task> operation)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        return [.. failures];
    }
    private sealed record Evidence(string Path, long Length, byte[] Hash);
}
