using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeResourceOwnerInventoryDiagnosticCapture
{
    internal static Task<(byte[] Payload, string Signature)> ReadAsync(PartitionHost partition, OrleansNode node,
        PrincipalRecord principal, NativeResourceOwnerInventoryDiagnosticRequest input,
        ResourceOwnerInventoryNativeClient client, IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, RequestCqrsProbeObserver observer, CancellationToken token)
        => NativeResourceOwnerInventoryDiagnosticDeadline.ReadAsync(limits, routing, clock,
            (work, expiresAt, originalToken) => CaptureAsync(partition, node, principal, input, client,
                nodeOptions, limits, clock, observer, work, expiresAt, originalToken), token);

    private static async Task<(byte[] Payload, string Signature)> CaptureAsync(PartitionHost partition, OrleansNode node,
        PrincipalRecord principal, NativeResourceOwnerInventoryDiagnosticRequest input,
        ResourceOwnerInventoryNativeClient client, IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
        TimeProvider clock, RequestCqrsProbeObserver observer, ReadExecutionBudget work,
        DateTimeOffset expiresAt, CancellationToken token)
    {
        var authentication = node.Authentication ?? throw Unavailable();
        var discovery = node.Discovery ?? throw Unavailable();
        var actual = discovery.Read();
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        work.ConstrainResultBytes(NativeResourceOwnerInventoryDiagnosticProtocol.MaximumPayloadBytes(limits));
        var request = partition.Database.AdmitResourceOwnerInventoryRequest(principal.Id, Guid.NewGuid(),
            input.TenantId, input.DatabaseId, input.ResourceName, input.PageSize, expiresAt, work);
        var authority = new ResourceOwnerInventoryAuthority(partition, client, nodeOptions, limits, clock);
        var inventory = await authority.ExecuteAsync(principal.Id, principal.PolicyEpoch, request, work, token).ConfigureAwait(false);
        work.Check();
        var witness = new NativeResourceOwnerInventoryDiagnosticWitness(input, actual, inventory,
            work.ReadBytes, work.ConstrainedNativeRecords);
        if (NativeSerialization.Measure(witness) > NativeResourceOwnerInventoryDiagnosticProtocol.MaximumPayloadBytes(limits))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        var bytes = NativeSerialization.Serialize(witness);
        var frame = NativeResourceOwnerInventoryDiagnosticProtocol.Frame(bytes, limits);
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        if (!ReferenceEquals(observer, node.CanonicalCutObserver) || !ReferenceEquals(authentication, node.Authentication)
            || !ReferenceEquals(discovery, node.Discovery) || discovery.Read() != actual)
        {
            throw Unavailable();
        }
        var current = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock);
        GrainRequestAuthority.RequireAdministrator(current);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        if (request.ExpiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        work.Check();
        token.ThrowIfCancellationRequested();
        return (bytes, authentication.SignDiscovery(frame, input.Nonce));
    }

    private static KeyLoadException Unavailable()
        => Errors.Fail(ErrorCode.OwnershipLost, ResourceOwnerInventoryNativeProtocol.Unavailable);
}
