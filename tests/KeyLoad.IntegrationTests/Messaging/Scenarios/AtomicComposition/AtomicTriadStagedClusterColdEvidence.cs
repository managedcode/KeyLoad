using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.StorageReplication;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class AtomicTriadStagedClusterColdEvidence
{
    private const int MaximumBytes = 65_536;
    private const int MinimumBytes = 1;
    private const int EndOfStream = -1;
    private const string Changed = "An original staged-triad cold receipt changed or exceeded its bound.";
    private readonly List<Evidence> originals = [];
    internal async Task CaptureAsync(string scenario, CancellationToken token)
    {
        var failures = new List<Exception>();
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        foreach (var node in QueueProducerClusterProtocol.Nodes)
        {
            var path = ProcessRuntimeOwnedReceipt.PathFor(repository, scenario, node);
            await ReadAsync(path, originals.Add, failures, token);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal async Task RequireAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var original in originals)
        {
            await ReadAsync(original.Path, item =>
            {
                if (item.Length != original.Length || !item.Hash.AsSpan().SequenceEqual(original.Hash))
                {
                    throw new InvalidOperationException(Changed);
                }
            }, failures, token);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task ReadAsync(string path, Action<Evidence> retain, List<Exception> failures, CancellationToken token)
    {
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
            {
                var length = stream.Length;
                if (length < MinimumBytes || length > MaximumBytes)
                {
                    throw new InvalidOperationException(Changed);
                }
                var bytes = new byte[checked((int)length)];
                await stream.ReadExactlyAsync(bytes, token);
                if (stream.ReadByte() != EndOfStream)
                {
                    throw new InvalidOperationException(Changed);
                }
                retain(new(path, length, SHA256.HashData(bytes)));
            }, failures);
        }, failures);
    }
    private sealed record Evidence(string Path, long Length, byte[] Hash);
}
