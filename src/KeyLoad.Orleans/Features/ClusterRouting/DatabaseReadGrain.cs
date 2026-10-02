using KeyLoad.Core;
using KeyLoad.Query;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Orleans;

/// <summary>Establishes a fresh quorum read cut and executes one persisted-authorized database capability.</summary>
/// <param name="codec">Signed request verifier bound to this actor's GUID.</param>
/// <param name="database">Borrowed canonical database; this actor never owns its files or apply gate.</param>
/// <param name="coordinator">Node-owned quorum barrier and canonical apply boundary.</param>
/// <param name="queries">Existing authorized query and live-query engine.</param>
/// <param name="search">Existing authorized text, vector and hybrid search engine.</param>
/// <param name="administration">Borrowed physical-node capabilities guarded by persisted administrator authority.</param>
/// <param name="clock">Runtime system clock for persisted principal expiry and query deadlines.</param>
/// <param name="diagnostics">Unexpected failure diagnostics; public replies contain only safe typed errors.</param>
[global::Orleans.GrainType(GrainRoutingProtocol.ReadAlias), global::Orleans.Placement.PreferLocalPlacement]
public sealed class DatabaseReadGrain(GrainRequestCodec codec, DatabaseEngine database, ICommitCoordinator coordinator,
    QueryEngine queries, SearchEngine search, INodeAdministration administration, TimeProvider clock,
    ILogger<DatabaseReadGrain> diagnostics) : Grain, IDatabaseReadGrain
{
    private readonly DatabaseEngine localDatabase = database;
    private readonly TimeProvider runtimeClock = clock;
    private readonly GrainCoreReadCapabilities core = new(database);
    private readonly GrainQueryReadCapabilities query = new(queries, search, clock);
    private readonly GrainBlobReadCapabilities blobs = new(database);

    /// <inheritdoc />
    /// <param name="signedRequest">The signed request bound to this unique read actor.</param>
    /// <param name="cancellationToken">Cancellation for quorum barrier and read execution.</param>
    /// <returns>A bounded safe reply from the requested persisted-authorized capability.</returns>
    public async Task<GrainOperationReply> ExecuteAsync(string signedRequest, CancellationToken cancellationToken)
    {
        var stage = GrainFailureStage.EnvelopeVerification;
        var requestId = Guid.Empty;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            requestId = this.GetPrimaryKey();
            var request = codec.VerifyRead(signedRequest, requestId);
            stage = GrainFailureStage.QuorumRead;
            await coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            stage = GrainFailureStage.CapabilityExecution;
            var result = await ReadAsync(request, cancellationToken).ConfigureAwait(true);
            stage = GrainFailureStage.ReplyEncoding;
            return GrainReplyFactory.Value(result, cancellationToken);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            return GrainReplyFactory.Failure(error, false, diagnostics, requestId, stage, cancellationToken);
        }
        finally
        {
            DeactivateOnIdle();
        }
    }

    private async Task<object?> ReadAsync(DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        var kind = request.Envelope.ReadKind!.Value;
        if (kind == GrainReadKind.Authenticate)
        {
            return GrainRequestAuthority.Authenticate(localDatabase, request.Payload, runtimeClock);
        }

        var principal = GrainRequestAuthority.Reload(localDatabase, request.Envelope.PrincipalId!, runtimeClock);
        if (GrainAdminDashboardCapabilities.Handles(kind))
        {
            return await GrainAdminDashboardCapabilities.ExecuteAsync(localDatabase, administration, principal,
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
            return query.Execute(kind, principal.Id, request.Payload, cancellationToken);
        }

        GrainPayloadJson.RequireNull(request.Payload);
        GrainRequestAuthority.RequireAdministrator(principal);
        cancellationToken.ThrowIfCancellationRequested();
        return kind switch
        {
            GrainReadKind.Backup => await administration.BackupAsync(cancellationToken).ConfigureAwait(true),
            GrainReadKind.Admission => administration.Admission(),
            GrainReadKind.NodeStatus => await administration.StatusAsync(cancellationToken).ConfigureAwait(true),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
