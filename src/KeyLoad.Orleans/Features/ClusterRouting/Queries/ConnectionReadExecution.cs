using KeyLoad.Core;
using KeyLoad.Diagnostics.Features.ResourceExecution;
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
/// <param name="context"></param>
internal sealed class ConnectionReadExecution(GrainRequestCodec codec, DatabaseEngine database, ICommitCoordinator coordinator,
    IServiceProvider services,
    TimeProvider clock, NativeRequestWorkOwner workOwner,
    ILogger diagnostics, IGrainContext context)
{
    private readonly DatabaseEngine localDatabase = database;
    private readonly NativeRequestWorkOwner requestWorkOwner = workOwner;
    private readonly ConnectionReadCapabilities capabilities = new(database, services, clock, codec, context);

    /// <summary>Executes one verified, call-local read without creating another activation.</summary>
    /// <param name="request">The verified operation whose native identity remains current.</param>
    /// <param name="cancellationToken">Cancellation for quorum barrier and read execution.</param>
    /// <returns>A bounded safe reply from the requested persisted-authorized capability.</returns>
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, CancellationToken cancellationToken)
    {
        using var work = new NativeCapabilityWorkLifetime(cancellationToken);
        try
        {
            AdmitRead(request, work);
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
                null);
        }
    }

    private void AdmitRead(DecodedGrainRequest request, NativeCapabilityWorkLifetime work)
    {
        work.Token.ThrowIfCancellationRequested();
        work.RequestId = request.Envelope.RequestId;
        if (request.Envelope.ReadKind is null)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest); }
        GrainIdentityContext.Validate(request.Envelope, work.RequestId);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.Admit(requestWorkOwner, work.RequestId, NativeRequestWorkKind.ReadCapability);
    }

    private async Task<GrainOperationReply> ExecuteReadAsync(DecodedGrainRequest request,
        NativeCapabilityWorkLifetime work)
    {
        if (request.Envelope.ReadKind == GrainReadKind.FollowerDocument)
        {
            var execution = new FollowerDocumentReadExecution(localDatabase, coordinator,
                services.GetRequiredService<KeyLoad.Replication.ReplicaConsensus>(),
                services.GetRequiredService<PhysicalShardRecord>(), codec);
            var follower = await execution.ExecuteAsync(request, context, work)
                .ConfigureAwait(true);
            work.Stage = GrainFailureStage.ReplyEncoding;
            return GrainReplyFactory.Value(value: follower, cancellationToken: work.Token, options: codec.ExecutionOptions);
        }
        work.Stage = GrainFailureStage.QuorumRead;
        var barrier = request.Envelope.ReadKind == GrainReadKind.Authenticate
            ? DatabasePhaseKind.AuthorizedAuthenticationBarrier : DatabasePhaseKind.AuthorizedOperationBarrier;
        work.BeginPhase(barrier);
        await coordinator.ReadBarrierAsync(work.Token).ConfigureAwait(true);
        work.CompletePhase();
        ValidateFreshRequest(codec, request, work.RequestId, work.Token);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.BeginPhase(DatabasePhaseKind.AuthorizedReadCapability);
        var result = await capabilities.ReadAsync(request, work.RequestId, work.Token).ConfigureAwait(true);
        work.CompletePhase();
        work.Stage = GrainFailureStage.ReplyEncoding;
        return GrainReplyFactory.Value(value: result, cancellationToken: work.Token, options: codec.ExecutionOptions);
    }

    internal static void ValidateFreshRequest(GrainRequestCodec codec, DecodedGrainRequest request,
        Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.ValidateScope(request.Envelope);
        GrainIdentityContext.Validate(request.Envelope, requestId);
    }

}
