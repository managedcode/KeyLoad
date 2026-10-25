using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.ClusterTests.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.EventStreams;

internal sealed class AggregateSnapshotRaceClusterCold
{
    private static readonly string[] Nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
    private const int MaximumReceiptBytes = 65_536;
    private const string ChangedReceipt = "An original aggregate snapshot cold receipt is unavailable or changed.";
    private readonly List<Evidence> originals = [];
    internal async Task RestartAsync(ClusterFixture fixture, CancellationToken token)
    {
        var scenario = ModelSqlClusterColdOwnership.Require(fixture).Claim(fixture);
        foreach (var node in Nodes)
        {
            if (File.Exists(PathFor(scenario, node)))
            {
                throw new InvalidOperationException(ChangedReceipt);
            }
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(token), failures);
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, scenario, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.App.ResourceNotifications
                .WaitForResourceHealthyAsync(node, WaitBehavior.WaitOnResourceUnavailable, token), failures);
        }
        foreach (var node in Nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => ReadAsync(PathFor(scenario, node), originals.Add, token), failures);
        }
        await ServerFailureObserver.ObserveAsync(() => RequireAsync(token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task RequireAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var expected in originals)
        {
            await ServerFailureObserver.ObserveAsync(() => ReadAsync(expected.Path, actual =>
            {
                if (actual.Length != expected.Length || !actual.Hash.AsSpan().SequenceEqual(expected.Hash))
                {
                    throw new InvalidOperationException(ChangedReceipt);
                }
            }, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static string PathFor(string scenario, string node)
        => ProcessRuntimeOwnedReceipt.PathFor(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, scenario, node);
    private static async Task ReadAsync(string path, Action<Evidence> accept, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan
            });
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (stream.Length is <= 0 or > MaximumReceiptBytes)
                {
                    throw new InvalidOperationException(ChangedReceipt);
                }
                accept(new(path, stream.Length, await SHA256.HashDataAsync(stream, token)));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private sealed record Evidence(string Path, long Length, byte[] Hash);
    internal static async Task<NodeStatus[]> StatusesAsync(ClusterFixture fixture, CancellationToken token)
    {
        var values = new List<NodeStatus>();
        foreach (var node in Nodes)
        {
            using var http = McpCallerHttp.Create(fixture, node);
            var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
            values.Add(await McpCallerAssertions.SdkSuccessAsync(await sdk.StatusAsync(token)));
        }
        return [.. values];
    }
    internal static async Task IdentitiesAsync(NodeStatus[] before, NodeStatus[] after)
    {
        await Assert.That(before.Length).IsEqualTo(3);
        await Assert.That(after.Length).IsEqualTo(3);
        await Assert.That(after.Select(node => node.NodeId).Distinct().Count()).IsEqualTo(3);
        for (var index = 0; index < after.Length; index++)
        {
            await Assert.That(after[index].NodeId).IsEqualTo(before[index].NodeId);
            await Assert.That(after[index].Incarnation).IsEqualTo(before[index].Incarnation);
            await Assert.That(after[index].ReadGeneration).IsGreaterThanOrEqualTo(before[index].ReadGeneration);
            await Assert.That(after[index].Position).IsGreaterThanOrEqualTo(before[index].Position);
            await Assert.That(before[index].SiloCount).IsEqualTo(3);
            await Assert.That(after[index].SiloCount).IsEqualTo(3);
            await Assert.That(after[index].Durability).IsEqualTo(before[index].Durability);
            await Assert.That(after[index].RoutingReady).IsTrue();
        }
    }
}
