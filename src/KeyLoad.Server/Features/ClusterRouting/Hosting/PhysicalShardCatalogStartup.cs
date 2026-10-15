using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PhysicalShardCatalogStartup(OrleansNode node, PartitionHost partition, IOptions<NodeOptions> nodeOptions,
    TimeProvider clock, IOptions<GrainRoutingOptions> routingOptions, IOptions<McpExecutionOptions> mcpExecutionOptions,
    ILogger<PhysicalShardCatalogStartup> logger)
{
    private const int AdmissionClosed = 0;
    private const int AdmissionReady = 1;
    private readonly NodeOptions options = nodeOptions.Value;
    private int ready;

    internal bool IsReady => Volatile.Read(ref ready) == AdmissionReady;

    internal async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var executionLifetime = routingOptions.Value.ExecutionLifetime;
        using var deadline = new CancellationTokenSource(executionLifetime, clock);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var token = operation.Token;
        await AwaitNativeReplicaPrerequisiteAsync(token).ConfigureAwait(false);
        if (!await TryVerifyCurrentCatalogAsync(token).ConfigureAwait(false))
        {
            var administrator = await AuthenticateBootstrapAdministratorAsync(token).ConfigureAwait(false);
            await SettleBootstrapAsync(administrator, token).ConfigureAwait(false);
        }
        node.OpenCatalogAdmission(this, token);
    }

    internal async Task EnsureAdmissionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.NotReady);
        }
        await VerifyCatalogAsync(cancellationToken).ConfigureAwait(false);
    }

    internal void OpenAdmission() => Volatile.Write(ref ready, AdmissionReady);

    internal void CloseAdmission() => Volatile.Write(ref ready, AdmissionClosed);

    private async Task AwaitNativeReplicaPrerequisiteAsync(CancellationToken cancellationToken)
    {
        var observation = new PhysicalShardCatalogPrerequisiteObservation();
        try
        {
            await AwaitNativeReplicaPrerequisiteCoreAsync(observation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            observation.Failed(logger, error);
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            observation.Failed(logger, error);
            throw;
        }
    }

    private Task AwaitNativeReplicaPrerequisiteCoreAsync(PhysicalShardCatalogPrerequisiteObservation observation,
        CancellationToken cancellationToken)
        => NativeReplicaStartupPrerequisite.WaitAsync(node, partition, clock, observation, cancellationToken);

    internal async Task<PrincipalRecord> AuthenticateBootstrapAdministratorAsync(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var codec = node.CatalogRequestCodec();
        var payload = NativeSerialization.Serialize(options.AdminKey);
        string signed;
        try
        {
            signed = codec.CreateRead(requestId, null, GrainReadKind.Authenticate, payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
        using var requestContext = node.OpenRequestContext(null, requestId, Guid.Empty, cancellationToken);
        var reply = await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var principal = McpNativeAuthentication.ReadPrincipal(payload: reply.Payload.Span, cancellationToken: cancellationToken, options: mcpExecutionOptions);
        if (!principal.ClusterAdministrator)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, PhysicalShardCatalogFence.AdministratorRequired);
        }
        return principal;
    }

    private async Task SubmitBootstrapAsync(PrincipalRecord administrator, Guid commandId, byte[] payload,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid();
        var codec = node.CatalogRequestCodec();
        var signed = codec.CreateCommand(requestId, administrator.Id,
            OperationKind.BootstrapPhysicalShardCatalog, commandId, payload);
        using var requestContext = node.OpenRequestContext(administrator, requestId, commandId, cancellationToken);
        _ = await node.ExecutePhysicalShardStartupRequestAsync(requestId, signed, command: true,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task SettleBootstrapAsync(PrincipalRecord administrator, CancellationToken cancellationToken)
    {
        var commandId = PhysicalShardCatalogIdentity.CreateBootstrapCommandId(options.PhysicalShardId);
        var request = new BootstrapPhysicalShardCatalogRequest(PhysicalShardCatalogStartupProtocol.Version,
            PhysicalShardCatalogStartupProtocol.ExpectedRevision, options.PhysicalShardId,
            partition.Configuration.Incarnation, partition.Configuration.VoterIds);
        var payload = NativeSerialization.Serialize(request);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await SubmitBootstrapAsync(administrator, commandId, payload, cancellationToken).ConfigureAwait(false);
                await VerifyCatalogAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (KeyLoadException error) when ((error.Code == ErrorCode.UnknownWriteOutcome
                && error.Message == ReplicaProtocol.InterruptedWrite)
                || (error.Code == ErrorCode.OwnershipLost
                && error.Message == ReplicaProtocol.NoLeader))
            {
                await Task.Delay(partition.Configuration.HeartbeatInterval, clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task VerifyCatalogAsync(CancellationToken cancellationToken)
    {
        if (!await TryVerifyCurrentCatalogAsync(cancellationToken).ConfigureAwait(false))
        {
            throw Errors.Fail(ErrorCode.NotFound, PhysicalShardCatalogLocalRead.CatalogMissing);
        }
    }

    private async Task<bool> TryVerifyCurrentCatalogAsync(CancellationToken cancellationToken)
    {
        var catalog = await PhysicalShardCatalogLocalRead.ReadAsync(partition, cancellationToken).ConfigureAwait(false);
        if (catalog is null)
        {
            return false;
        }
        if (!PhysicalShardCatalogFence.Matches(catalog, options.PhysicalShardId,
                partition.Configuration.Incarnation, partition.Configuration.VoterIds))
        {
            CloseAdmission();
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalShardCatalogFence.Mismatch);
        }
        return true;
    }
}
