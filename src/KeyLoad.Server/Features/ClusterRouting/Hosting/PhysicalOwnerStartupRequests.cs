using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PhysicalOwnerStartupRequests(OrleansNode node, IOptions<NodeOptions> nodeOptions,
    IOptions<McpExecutionOptions> authenticationOptions)
{
    private const string AdministratorRequired = "Physical owner registration requires a persisted cluster administrator.";
    private const string InvalidStatus = "The native physical owner status reply is invalid.";

    internal async Task<PrincipalRecord> AuthenticateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestId = Guid.NewGuid();
        var payload = NativeSerialization.Serialize(nodeOptions.Value.AdminKey);
        string signed;
        try
        { signed = node.CatalogRequestCodec().CreateRead(requestId, null, GrainReadKind.Authenticate, payload); }
        finally
        { CryptographicOperations.ZeroMemory(payload); }
        using var identity = node.OpenRequestContext(null, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var principal = McpNativeAuthentication.ReadPrincipal(reply.Payload.Span, authenticationOptions, cancellationToken);
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, AdministratorRequired); }
        return principal;
    }

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
