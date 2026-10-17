using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ResourceExecution.Execution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PhysicalOwnerStartupRequests(OrleansNode node, IOptions<NodeOptions> nodeOptions,
    IOptions<McpExecutionOptions> authenticationOptions)
{
    private const string InvalidStatus = "The native physical owner status reply is invalid.";

    internal Task<PrincipalRecord> AuthenticateAsync(CancellationToken cancellationToken)
        => NativeAdministratorAuthenticationRequests.StartupAsync(node, nodeOptions.Value.AdminKey,
            authenticationOptions, cancellationToken);

    internal Task<PrincipalRecord> AuthenticateAsync(KeyLoad.Core.ReadExecutionBudget work,
        DateTimeOffset originalExpiry, CancellationToken cancellationToken)
        => NativeAdministratorAuthenticationRequests.AuthenticateAsync(node, nodeOptions.Value.AdminKey,
            authenticationOptions, work, originalExpiry, cancellationToken);

    internal Task<PrincipalRecord> AuthenticateAsync(KeyLoad.Core.ReadExecutionBudget work,
        ReadExecutionBudgetReadGrant metadataGrant, DateTimeOffset originalExpiry,
        CancellationToken cancellationToken)
        => NativeAdministratorAuthenticationRequests.AuthenticateAsync(node, nodeOptions.Value.AdminKey,
            authenticationOptions, work, metadataGrant, originalExpiry, cancellationToken);

    internal async Task<NodeStatus> ReadStatusAsync(PrincipalRecord principal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(GrainNativeContracts.NoDtoMarker);
        var signed = node.CatalogRequestCodec().CreateRead(requestId, principal.Id, GrainReadKind.NodeStatus, payload);
        using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return GrainNativePayload.Read<GrainValue>(reply.Payload).Value as NodeStatus
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidStatus);
    }

    internal async Task<GrainOperationReply> RegisterAsync(PrincipalRecord principal,
        RegisterPhysicalOwnerV1 request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commandId = PhysicalOwnerRegistrationIdentity.Create(request);
        var requestId = Guid.NewGuid();
        var signed = node.CatalogRequestCodec().CreateCommand(requestId, principal.Id,
            OperationKind.RegisterPhysicalOwner, commandId, NativeSerialization.Serialize(request));
        using var identity = node.OpenRequestContext(principal, requestId, commandId, cancellationToken);
        return await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
