using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeColdIdentities
{
    private const string GuidFormat = "D";
    private const string Invalid = "The original SDK status does not bind all six stopped canonical store owners.";

    internal static async Task<IReadOnlyDictionary<string, Guid>> ReadAsync(TwoRf3MembershipWave wave,
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> signedOwners, CancellationToken token)
    {
        var nodes = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var distinct = new HashSet<Guid>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            token.ThrowIfCancellationRequested();
            var status = await RequestCqrsRf3Callers.RunOwnedAsync(wave.Application, node, wave.Profile.AdminKey,
                async original => await McpCallerAssertions.SdkSuccessAsync(
                    await original.Sdk.StatusAsync(token).ConfigureAwait(false)).ConfigureAwait(false), token)
                .ConfigureAwait(false);
            if (!signedOwners.TryGetValue(node, out var owner) || owner is null
                || status.Incarnation != owner.Incarnation
                || !Guid.TryParseExact(status.NodeId, GuidFormat, out var originalNodeId)
                || originalNodeId == Guid.Empty || originalNodeId.ToString(GuidFormat) != status.NodeId
                || !distinct.Add(originalNodeId))
            { throw new InvalidOperationException(Invalid); }
            nodes.Add(node, originalNodeId);
        }
        return nodes;
    }
}
