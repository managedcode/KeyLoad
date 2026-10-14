using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class RequestCqrsCohortOwners
{
    internal const string Key = "KeyLoadTests:NativeCohortOwner";
    private static readonly ConcurrentDictionary<string, RequestCqrsCohortRuntimeFixture> Owners = new();
    internal static void Register(string id, RequestCqrsCohortRuntimeFixture owner)
    {
        if (!Owners.TryAdd(id, owner))
        {
            throw new InvalidOperationException("Duplicate native cohort owner.");
        }
    }
    internal static RequestCqrsCohortRuntimeFixture Resolve(IConfiguration configuration)
        => Owners.TryGetValue(configuration[Key] ?? string.Empty, out var owner) ? owner
            : throw new InvalidOperationException("The actual native cohort owner is absent.");
    internal static void Release(string id) => Owners.TryRemove(id, out _);
}
