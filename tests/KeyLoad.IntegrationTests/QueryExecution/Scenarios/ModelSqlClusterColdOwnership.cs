using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed class ModelSqlClusterColdOwnership
{
    private static readonly ConditionalWeakTable<ClusterFixture, ModelSqlClusterColdOwnership> Owners = new();
    private readonly Guid incarnation;
    private long ordinal;
    private Evidence[] previous = [];
    private const string Changed = "An original owned SQL cold restart receipt changed.";

    private ModelSqlClusterColdOwnership(ClusterFixture fixture)
        => incarnation = ClusterFixtureProfile.ReadProfile(fixture.Root).Incarnation;

    internal static ModelSqlClusterColdOwnership Require(ClusterFixture fixture)
        => Owners.GetValue(fixture, static owner => new(owner));

    internal string Claim(ClusterFixture fixture)
    {
        if (ClusterFixtureProfile.ReadProfile(fixture.Root).Incarnation != incarnation)
        {
            throw new InvalidOperationException(Changed);
        }
        return ContainerRuntimeOwnedReceipt.Scenario(incarnation, Interlocked.Increment(ref ordinal));
    }

    internal async Task RequirePreviousAsync(CancellationToken token)
    {
        foreach (var item in previous)
        {
            var actual = await ReadEvidenceAsync(item.Path, token);
            if (actual.Length != item.Length || !actual.Hash.AsSpan().SequenceEqual(item.Hash))
            {
                throw new InvalidOperationException(Changed);
            }
        }
    }

    internal async Task CaptureAsync(string scenario, CancellationToken token)
    {
        await RequirePreviousAsync(token);
        var repository = ClusterFixtureDiagnostics.FindRepositoryRoot().FullName;
        var captures = new List<Evidence>();
        foreach (var node in TopicSqlClusterProtocol.Nodes)
        {
            var path = ContainerRuntimeOwnedReceipt.PathFor(repository, scenario, node);
            captures.Add(await ReadEvidenceAsync(path, token));
        }
        previous = [.. captures];
    }

    private static async Task<Evidence> ReadEvidenceAsync(string path, CancellationToken token)
    {
        Evidence? result = null;
        var failures = new List<Exception>();
        await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var stream = new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.Read,
                Options = FileOptions.Asynchronous
            });
            await global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                result = new(path, stream.Length, await SHA256.HashDataAsync(stream, token));
            }, failures);
        }, failures);
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private sealed record Evidence(string Path, long Length, byte[] Hash);
}
