using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeResourceRetirementDiagnosticCapture
{
    private const int MetadataHeadroomDivisor = 2;

    internal static Task<(byte[] Payload, string Signature)> ReadAsync(PartitionHost partition, OrleansNode node,
        PrincipalRecord principal, NativeResourceRetirementDiagnosticRequest input,
        ResourceOwnerInventoryNativeClient client, IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, RequestCqrsProbeObserver observer, CancellationToken token)
        => NativeResourceOwnerInventoryDiagnosticDeadline.ReadAsync(limits, routing, clock,
            (work, expiresAt, originalToken) => CaptureAsync(partition, node, principal, input, client,
                nodeOptions, limits, clock, observer, work, expiresAt, originalToken), token);

    private static async Task<(byte[] Payload, string Signature)> CaptureAsync(PartitionHost partition, OrleansNode node,
        PrincipalRecord principal, NativeResourceRetirementDiagnosticRequest input,
        ResourceOwnerInventoryNativeClient client, IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
        TimeProvider clock, RequestCqrsProbeObserver observer, ReadExecutionBudget work,
        DateTimeOffset expiresAt, CancellationToken token)
    {
        work.Check();
        var bytes = work.RemainingReadGrantBytes / MetadataHeadroomDivisor;
        var records = work.RemainingReadGrantRecords / MetadataHeadroomDivisor;
        if (bytes <= ResourceRetirementReadContract.NoReadBytes || records <= ResourceRetirementReadContract.NoRecords)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
        var metadata = work.CreateReadGrant(bytes, records);
        var failures = new List<Exception>();
        (byte[] Payload, string Signature) result = default;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            result = await CaptureOwnedAsync(partition, node, principal, input, client, nodeOptions, limits,
                clock, observer, work, metadata, expiresAt, token).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(metadata), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static async Task<(byte[] Payload, string Signature)> CaptureOwnedAsync(PartitionHost partition, OrleansNode node,
        PrincipalRecord principal, NativeResourceRetirementDiagnosticRequest input,
        ResourceOwnerInventoryNativeClient client, IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
        TimeProvider clock, RequestCqrsProbeObserver observer, ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadata, DateTimeOffset expiresAt, CancellationToken token)
    {
        var authentication = node.Authentication ?? throw Unavailable();
        var discovery = node.Discovery ?? throw Unavailable();
        var actual = discovery.Read();
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        work.ConstrainResultBytes(NativeResourceRetirementDiagnosticProtocol.MaximumPayloadBytes(limits));
        var (state, preparation) = await ExecuteAsync(partition, client, principal, input, nodeOptions,
            limits, clock, expiresAt, work, metadata, token).ConfigureAwait(false);
        work.Check();
        RequirePublicationPrincipal(partition, principal, clock, work, metadata);
        var witness = new NativeResourceRetirementDiagnosticWitness(input, actual, state,
            work.ReadBytes, work.ConstrainedNativeRecords)
        {
            Preparation = preparation
        };
        if (NativeSerialization.Measure(witness) > NativeResourceRetirementDiagnosticProtocol.MaximumPayloadBytes(limits))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementNativeProtocol.Unavailable);
        }
        var bytes = NativeSerialization.Serialize(witness);
        var frame = NativeResourceRetirementDiagnosticProtocol.Frame(bytes, limits);
        NativeSiloDiscoveryDiagnosticEndpoints.RequireCurrent(node, actual);
        if (!ReferenceEquals(observer, node.CanonicalCutObserver) || !ReferenceEquals(authentication, node.Authentication)
            || !ReferenceEquals(discovery, node.Discovery) || discovery.Read() != actual)
        {
            throw Unavailable();
        }
        if (expiresAt <= clock.GetUtcNow())
        {
            throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
        }
        work.Check();
        token.ThrowIfCancellationRequested();
        return (bytes, authentication.SignDiscovery(frame, input.Nonce));
    }

    private static async Task<(ResourceRetirementStateV1? State, ResourceRetirementPreparationStateV1? Preparation)> ExecuteAsync(PartitionHost partition,
        ResourceOwnerInventoryNativeClient client, PrincipalRecord principal, NativeResourceRetirementDiagnosticRequest input,
        IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits, TimeProvider clock,
        DateTimeOffset expiresAt, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata, CancellationToken token)
    {
        if (input.Operation == ResourceRetirementDiagnosticOperation.ReadPreparation)
        {
            var probe = new ResourceRetirementStateProbe(partition, client, nodeOptions, limits, clock);
            return (null, await probe.ReadPreparationAsync(principal.Id, principal.PolicyEpoch, input.Request,
                expiresAt, work, metadata, token).ConfigureAwait(false));
        }
        if (input.Operation == ResourceRetirementDiagnosticOperation.ReadState)
        {
            var probe = new ResourceRetirementStateProbe(partition, client, nodeOptions, limits, clock);
            return (await probe.ReadAsync(principal.Id, principal.PolicyEpoch, input.Request,
                expiresAt, work, metadata, token).ConfigureAwait(false), null);
        }
        var inventory = new ResourceOwnerInventoryAuthority(partition, client, nodeOptions, limits, clock);
        var authority = new ResourceRetirementAuthority(partition, client, inventory, nodeOptions, limits, clock);
        return (await authority.ExecuteAsync(principal.Id, principal.PolicyEpoch, input.Request,
            input.PageSize, expiresAt, work, metadata, token).ConfigureAwait(false), null);
    }

    private static void RequirePublicationPrincipal(PartitionHost partition, PrincipalRecord principal,
        TimeProvider clock, ReadExecutionBudget work, ReadExecutionBudgetReadGrant metadata)
    {
        using var scope = work.EnterReadGrant(metadata);
        var current = GrainRequestAuthority.Reload(partition.Database, principal.Id, clock, work);
        GrainRequestAuthority.RequireAdministrator(current);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementNativeProtocol.Unavailable);
        }
    }

    private static KeyLoadException Unavailable()
        => Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
}
