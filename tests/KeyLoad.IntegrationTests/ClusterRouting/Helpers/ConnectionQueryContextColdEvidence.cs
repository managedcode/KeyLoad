using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class ConnectionQueryContextColdEvidence
{
    private Evidence[] originals = [];
    internal async Task<string> ClaimAsync(Guid incarnation, long ordinal, CancellationToken token)
    {
        await RequireAsync(token);
        return ContainerRuntimeOwnedReceipt.Scenario(incarnation, ordinal);
    }
    internal async Task CaptureAsync(string scenario, CancellationToken token)
    {
        await RequireAsync(token);
        var captured = new List<Evidence>();
        var failures = new List<Exception>();
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var path = ContainerRuntimeOwnedReceipt.PathFor(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName, scenario, node);
            await ServerFailureObserver.ObserveAsync(async () => captured.Add(await ReadAsync(path, token)), failures);
        }
        originals = [.. originals, .. captured];
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task RequireAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var current = await ReadAsync(original.Path, token);
                if (current.Length != original.Length || !current.Hash.AsSpan().SequenceEqual(original.Hash))
                {
                    throw new InvalidOperationException("An original query cold receipt changed.");
                }
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<Evidence> ReadAsync(string path, CancellationToken token)
    {
        var captured = new List<Evidence>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous
            });
            await ServerFailureObserver.ObserveAsync(async () =>
                captured.Add(new(path, stream.Length, await SHA256.HashDataAsync(stream, token))), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return captured.Single();
    }
    private sealed record Evidence(string Path, long Length, byte[] Hash);
}
