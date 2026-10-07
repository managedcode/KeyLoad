using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Binds native test-cluster configuration to its exact owning database fixture.</summary>
internal static class RequestCqrsFixtureOwners
{
    internal const string ConfigurationKey = "KeyLoadTests:RequestCqrsFixtureOwner";
    private const string Missing = "The native request fixture owner is not registered.";
    private static readonly ConcurrentDictionary<string, RequestCqrsClusterFixture> Owners = new(StringComparer.Ordinal);

    internal static void Register(string id, RequestCqrsClusterFixture fixture)
    {
        if (!Owners.TryAdd(id, fixture))
        { throw new InvalidOperationException(Missing); }
    }

    internal static RequestCqrsClusterFixture Resolve(IConfiguration configuration)
    {
        var id = configuration[ConfigurationKey];
        return id is not null && Owners.TryGetValue(id, out var fixture)
            ? fixture : throw new InvalidOperationException(Missing);
    }

    internal static void Release(string id) => Owners.TryRemove(id, out _);
}
