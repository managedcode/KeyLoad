using System.Security.Cryptography;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

internal sealed class PhysicalShardCatalogStartup(OrleansNode node, PartitionHost partition, NodeOptions options,
    TimeProvider clock)
{
    private string? administratorId;
    private int ready;

    internal bool IsReady => Volatile.Read(ref ready) == 1;

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(GrainRequestStreamProtocol.ExecutionLifetime, clock);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = operation.Token;
        var administrator = await AuthenticateAdministratorAsync(token).ConfigureAwait(false);
        await SubmitBootstrapAsync(administrator, token).ConfigureAwait(false);
        await VerifyCatalogAsync(administrator.Id, token).ConfigureAwait(false);
        node.OpenCatalogAdmission(this, administrator.Id, token);
    }

    internal async Task EnsureAdmissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var principalId = Volatile.Read(ref administratorId);
        if (!IsReady || principalId is null)
        { throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady); }
        await VerifyCatalogAsync(principalId, cancellationToken).ConfigureAwait(false);
    }

    internal void OpenAdmission(string principalId)
    {
        Volatile.Write(ref administratorId, principalId);
        Volatile.Write(ref ready, 1);
    }

    internal void CloseAdmission() => Volatile.Write(ref ready, 0);

    private async Task<PrincipalRecord> AuthenticateAdministratorAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var codec = node.CatalogRequestCodec();
        var payload = NativeSerialization.Serialize(options.AdminKey);
        string signed;
        try
        { signed = codec.CreateRead(requestId, null, GrainReadKind.Authenticate, payload); }
        finally
        { CryptographicOperations.ZeroMemory(payload); }
        using var requestContext = node.OpenRequestContext(null, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var principal = McpNativeAuthentication.ReadPrincipal(reply.Payload.Span, cancellationToken);
        if (!principal.ClusterAdministrator)
        { throw Errors.Fail(ErrorCode.PermissionDenied, PhysicalShardCatalogFence.AdministratorRequired); }
        return principal;
    }

    private async Task SubmitBootstrapAsync(PrincipalRecord administrator, CancellationToken cancellationToken)
    {
        var commandId = PhysicalShardCatalogIdentity.CreateBootstrapCommandId(options.PhysicalShardId);
        var requestId = Guid.NewGuid();
        var request = new BootstrapPhysicalShardCatalogRequest(PhysicalShardCatalogStartupProtocol.Version,
            PhysicalShardCatalogStartupProtocol.ExpectedRevision, options.PhysicalShardId,
            partition.Configuration.Incarnation, partition.Configuration.VoterIds);
        var payload = NativeSerialization.Serialize(request);
        var codec = node.CatalogRequestCodec();
        var signed = codec.CreateCommand(requestId, administrator.Id,
            OperationKind.BootstrapPhysicalShardCatalog, commandId, payload);
        using var requestContext = node.OpenRequestContext(administrator, requestId, commandId, cancellationToken);
        _ = await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task VerifyCatalogAsync(string principalId, CancellationToken cancellationToken)
    {
        await partition.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var catalog = partition.Database.ReadPhysicalShardCatalog(principalId);
        if (!PhysicalShardCatalogFence.Matches(catalog, options.PhysicalShardId,
                partition.Configuration.Incarnation, partition.Configuration.VoterIds))
        {
            CloseAdmission();
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.Mismatch);
        }
    }
}
