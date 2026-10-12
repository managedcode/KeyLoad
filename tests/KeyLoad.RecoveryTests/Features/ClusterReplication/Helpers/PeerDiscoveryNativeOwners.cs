using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
namespace KeyLoad.RecoveryTests;

internal static class PeerDiscoveryNativeOwners
{
    internal const string Key = "KeyLoadTests:RecoveryDiscoveryOwner";
    private static readonly ConcurrentDictionary<string, PeerDiscoveryNativeCluster> Owners = new(StringComparer.Ordinal);
    internal static void Register(string id, PeerDiscoveryNativeCluster owner)
    {
        if (!Owners.TryAdd(id, owner))
        { throw new InvalidOperationException("Duplicate discovery fixture owner."); }
    }
    internal static PeerDiscoveryNativeCluster Resolve(IConfiguration configuration)
        => Owners.TryGetValue(configuration[Key] ?? string.Empty, out var owner) ? owner
            : throw new InvalidOperationException("Missing discovery fixture owner.");
    internal static void Release(string id) => Owners.TryRemove(id, out _);
}
