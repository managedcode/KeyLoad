using KeyLoad.Core;
using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

/// <summary>Establishes a fresh quorum read cut and executes one persisted-authorized database capability.</summary>
/// <param name="codec">Signed request verifier bound to this actor's GUID.</param>
/// <param name="database">Borrowed canonical database; this actor never owns its files or apply gate.</param>
/// <param name="coordinator">Node-owned quorum barrier and canonical apply boundary.</param>
/// <param name="services">Borrowed capability services resolved only after the authorized operation is selected.</param>
/// <param name="clock">Runtime system clock for persisted principal expiry and query deadlines.</param>
/// <param name="workOwner">Silo-local admission and cancellation owner for verified reads.</param>
/// <param name="diagnostics">Unexpected failure diagnostics; public replies contain only safe typed errors.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.ReadAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class DatabaseReadGrain(GrainRequestCodec codec, DatabaseEngine database, ICommitCoordinator coordinator,
    IServiceProvider services,
    TimeProvider clock, NativeRequestWorkOwner workOwner,
    ILogger<DatabaseReadGrain> diagnostics)
    : Grain, IDatabaseReadGrain
{
    private readonly DatabaseEngine localDatabase = database;
    private readonly TimeProvider runtimeClock = clock;
    private readonly NativeRequestWorkOwner requestWorkOwner = workOwner;
    private readonly GrainCoreReadCapabilities core = new(database);
    private GrainQueryReadCapabilities? query;
    private GrainQueryReadCapabilities Query => query ??= new(services.GetRequiredService<QueryEngine>(),
        services.GetRequiredService<SearchEngine>(), runtimeClock, services.GetRequiredService<PhysicalShardRecord>());
    private INodeAdministration Administration => services.GetRequiredService<INodeAdministration>();
    private readonly GrainBlobReadCapabilities blobs = new(database);

    /// <inheritdoc />
    /// <param name="signedRequest">The signed request bound to this unique read actor.</param>
    /// <param name="cancellationToken">Cancellation for quorum barrier and read execution.</param>
    /// <returns>A bounded safe reply from the requested persisted-authorized capability.</returns>
    public async Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
    {
        using var work = new NativeCapabilityWorkLifetime(cancellationToken);
        try
        {
            var request = AdmitRead(signedRequest, work);
            return await ExecuteReadAsync(request, work).ConfigureAwait(true);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            work.PrimaryError = error;
            try
            {
                return GrainReplyFactory.Failure(error: error, command: false, diagnostics: diagnostics, requestId: work.RequestId, stage: work.Stage,
                    cancellationToken: work.Token, options: codec.ExecutionOptions);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                work.PrimaryError = NativeCapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
                throw;
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                work.PrimaryError = NativeCapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
                throw;
            }
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            work.PrimaryError = error;
            throw;
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            work.PrimaryError = error;
            throw;
        }
        finally
        {
            NativeCapabilityWorkLifetime.Settle(work.PrimaryError, work, work.FailureTelemetry(),
                DeactivateOnIdle);
        }
    }

    private DecodedGrainRequest AdmitRead(string signedRequest, NativeCapabilityWorkLifetime work)
    {
        work.Token.ThrowIfCancellationRequested();
        work.RequestId = this.GetPrimaryKey();
        var request = codec.VerifyRead(signedRequest, work.RequestId);
        GrainIdentityContext.Validate(request.Envelope, work.RequestId);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.Admit(requestWorkOwner, work.RequestId, NativeRequestWorkKind.ReadCapability);
        return request;
    }

    private async Task<GrainOperationReply> ExecuteReadAsync(DecodedGrainRequest request,
        NativeCapabilityWorkLifetime work)
    {
        work.Stage = GrainFailureStage.QuorumRead;
        var barrier = request.Envelope.ReadKind == GrainReadKind.Authenticate
            ? DatabasePhaseKind.AuthorizedAuthenticationBarrier : DatabasePhaseKind.AuthorizedOperationBarrier;
        work.BeginPhase(barrier);
        await coordinator.ReadBarrierAsync(work.Token).ConfigureAwait(true);
        work.CompletePhase();
        ValidateFreshRequest(request, work.RequestId, work.Token);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.BeginPhase(DatabasePhaseKind.AuthorizedReadCapability);
        var result = await ReadAsync(request, work.RequestId, work.Token).ConfigureAwait(true);
        work.CompletePhase();
        work.Stage = GrainFailureStage.ReplyEncoding;
        return GrainReplyFactory.Value(value: result, cancellationToken: work.Token, options: codec.ExecutionOptions);
    }

    private void ValidateFreshRequest(DecodedGrainRequest request, Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.ValidateScope(request.Envelope);
        GrainIdentityContext.Validate(request.Envelope, requestId);
    }

    private async Task<object?> ReadAsync(DecodedGrainRequest request, Guid requestId,
        CancellationToken cancellationToken)
    {
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.Authenticate)
        {
            return GrainRequestAuthority.Authenticate(localDatabase, request.Payload, runtimeClock);
        }

        if (codec.HasPhaseObserver)
        {
            await codec.ObservePhaseAsync(request, GrainRequestPhase.AuthorizationReload,
                ((IGrainBase)this).GrainContext, cancellationToken).ConfigureAwait(true);
            ValidateFreshRequest(request, requestId, cancellationToken);
        }

        var principal = GrainRequestAuthority.ReloadForRequest(localDatabase, request.Envelope, runtimeClock);
        if (GrainControlledReadCapabilities.Handles(kind))
        {
            return GrainControlledReadCapabilities.Execute(localDatabase, services, runtimeClock,
                principal, request, cancellationToken);
        }
        if (kind == GrainReadKind.PartitionMovementCapture)
        {
            return await PartitionMovementCaptureExecution.ExecuteAsync(
                services.GetRequiredService<INativePartitionMovementCapture>(), principal,
                GrainNativePayload.Read<PartitionMovementCaptureCapability>(request.Payload),
                cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.TextMaintenance)
        {
            GrainRequestAuthority.RequireAdministrator(principal);
            return await services.GetRequiredService<INativeTextMaintenance>().ExecuteAsync(principal,
                GrainNativePayload.Read<TextMaintenanceCapabilityRequest>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.Document
            && services.GetService<IRemoteDocumentReadRouter>() is { } remoteDocuments)
        {
            return await remoteDocuments.ReadAsync(request.Envelope, principal,
                GrainNativePayload.Read<GetDocumentRequest>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.PartitionQuery
            && services.GetService<IRemotePartitionQueryRouter>() is { } remoteQueries)
        {
            return await remoteQueries.ReadAsync(request.Envelope, principal,
                GrainNativePayload.ReadPublicInput<PartitionQueryRequestV1>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        if (kind == GrainReadKind.AnnMaintenance)
        {
            GrainRequestAuthority.RequireAdministrator(principal);
            return await services.GetRequiredService<INativeAnnMaintenance>().ExecuteAsync(principal,
                GrainNativePayload.Read<AnnMaintenanceCapabilityRequest>(request.Payload), cancellationToken).ConfigureAwait(true);
        }
        return await ReadNativeCapabilityAsync(kind, principal, request, cancellationToken).ConfigureAwait(true);
    }

    private async Task<object?> ReadNativeCapabilityAsync(GrainReadKind kind, PrincipalRecord principal,
        DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        if (RuntimeJournalRequestScope.Handles(kind))
        {
            return RuntimeJournalReadCapabilities.Execute(localDatabase, principal.Id, kind, request.Payload, cancellationToken);
        }
        if (GrainAdminDashboardCapabilities.Handles(kind))
        {
            return await GrainAdminDashboardCapabilities.ExecuteAsync(localDatabase, Administration, principal,
                kind, request.Payload, cancellationToken).ConfigureAwait(true);
        }
        if (GrainBlobReadCapabilities.Handles(kind))
        {
            return blobs.Execute(kind, principal.Id, request.Payload, cancellationToken);
        }
        if (GrainCoreReadCapabilities.Handles(kind))
        {
            return core.Execute(kind, principal.Id, request.Payload, cancellationToken);
        }

        if (kind is not (GrainReadKind.Backup or GrainReadKind.Admission or GrainReadKind.NodeStatus))
        {
            return await Query.ExecuteAsync(kind, principal.Id, request.Payload, cancellationToken).ConfigureAwait(true);
        }

        GrainNativePayload.RequireNoDto(request.Payload);
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Backup => await Administration.BackupAsync(cancellationToken).ConfigureAwait(true),
            GrainReadKind.Admission => Administration.Admission(),
            GrainReadKind.NodeStatus => await Administration.StatusAsync(cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
