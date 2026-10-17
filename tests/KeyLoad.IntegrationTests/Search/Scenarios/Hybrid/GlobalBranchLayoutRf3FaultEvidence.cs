using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class GlobalBranchLayoutRf3FaultEvidence
{
    private const string Prefix = "global-ranking-required-shard-unavailable-";
    private const string Invalid = "An original global-ranking fault receipt is unavailable or changed.";
    private const string GuidFormat = "N";
    private const int MaximumReceiptBytes = 65_536;
    private readonly List<Evidence> originals = [];
    private string scenario = string.Empty;

    internal string Begin(Guid incarnation, bool official)
    {
        if (incarnation == Guid.Empty || scenario.Length != 0)
        {
            throw new InvalidOperationException(Invalid);
        }
        scenario = Prefix + incarnation.ToString(GuidFormat) + (official ? "-mcp-" : "-sdk-")
            + Guid.NewGuid().ToString(GuidFormat);
        foreach (var node in new[] { TwoRf3MembershipProtocol.Node4, TwoRf3MembershipProtocol.Node5, TwoRf3MembershipProtocol.Node6 })
        {
            if (File.Exists(PathFor(node)))
            {
                throw new InvalidOperationException(Invalid);
            }
        }
        return scenario;
    }

    internal async Task CaptureAsync(IEnumerable<string> nodes, CancellationToken token)
    {
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => ReadAsync(PathFor(node), originals.Add, token), failures);
        }
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
                    throw new InvalidOperationException(Invalid);
                }
            }, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private string PathFor(string node)
    {
        if (scenario.Length == 0 || node is not (TwoRf3MembershipProtocol.Node4 or TwoRf3MembershipProtocol.Node5 or TwoRf3MembershipProtocol.Node6))
        {
            throw new InvalidOperationException(Invalid);
        }
        return Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            ContainerRuntimeProtocol.ArtifactsDirectory, ContainerRuntimeProtocol.QualificationDirectory,
            string.Format(CultureInfo.InvariantCulture, ContainerRuntimeProtocol.ReceiptFileName, scenario, node));
    }

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
                    throw new InvalidOperationException(Invalid);
                }
                accept(new(path, stream.Length, await SHA256.HashDataAsync(stream, token)));
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private sealed record Evidence(string Path, long Length, byte[] Hash);
}
