using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Diagnostics.ResourceExecution;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Clustering;

/// <summary>Establishes a fresh storage read cut and executes one persisted-authorized database capability.</summary>
/// <param name="codec">Signed request verifier bound to this actor's GUID.</param>
/// <param name="database">Borrowed canonical database; this actor never owns its files or apply gate.</param>
/// <param name="services">Borrowed capability services resolved only after the authorized operation is selected.</param>
/// <param name="clock">Runtime system clock for persisted principal expiry and query deadlines.</param>
/// <param name="workOwner">Silo-local admission and cancellation owner for verified reads.</param>
/// <param name="diagnostics">Unexpected failure diagnostics; public replies contain only safe typed errors.</param>
/// <param name="context"></param>
internal sealed class ConnectionReadExecution(GrainRequestCodec codec, DatabaseEngine database,
    IServiceProvider services,
    TimeProvider clock, RequestWorkOwner workOwner,
    ILogger diagnostics, IGrainContext context)
{
    private readonly RequestWorkOwner requestWorkOwner = workOwner;
    private readonly ConnectionReadCapabilities capabilities = new(database, services, clock, codec, context);

    /// <summary>Executes one verified, call-local read without creating another activation.</summary>
    /// <param name="request">The verified operation whose execution identity remains current.</param>
    /// <param name="originalBinding">Original operation lifetime binding retained by this read capability.</param>
    /// <param name="observeOriginalFailure">Records the original failure on the owning operation before reply encoding or propagation.</param>
    /// <param name="cancellationToken">Cancellation for the read barrier and read execution.</param>
    /// <returns>A bounded safe reply from the requested persisted-authorized capability.</returns>
    internal async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest request, IResourceLifetimeWorkBinding originalBinding, Action<Exception> observeOriginalFailure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(originalBinding) /* KLD0041: BCL null validation accepts the originalBinding IResourceLifetimeWorkBinding reference through its object parameter. */;
        ArgumentNullException.ThrowIfNull(observeOriginalFailure) /* KLD0041: BCL null validation accepts the observeOriginalFailure Action reference through its object parameter. */;
        await using var work = new CapabilityWorkLifetime(cancellationToken);
        try
        {
            AdmitRead(request, work, originalBinding);
            return await ExecuteReadAsync(request, work);
        }
        catch (Exception error) when (GrainBoundaryErrors.Handles(error))
        {
            work.PrimaryError = error;
            observeOriginalFailure(error);
            try
            {
                return GrainReplyFactory.Failure(error: error, command: false, diagnostics: diagnostics, requestId: work.RequestId, stage: work.Stage,
                    cancellationToken: work.Token);
            }
            catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
            {
                work.PrimaryError = CapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
                throw;
            }
            catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
            {
                work.PrimaryError = CapabilityWorkLifetime.CombinePrimary(work.PrimaryError, failure);
                throw;
            }
        }
        catch (Exception error) when (CqrsBoundaryErrors.IsNonFatal(error))
        {
            work.PrimaryError = error;
            observeOriginalFailure(error);
            throw;
        }
        catch (Exception error) when (!CqrsBoundaryErrors.IsNonFatal(error))
        {
            work.PrimaryError = error;
            observeOriginalFailure(error);
            throw;
        }
        finally
        {
            await CapabilityWorkLifetime.SettleAsync(work.PrimaryError, work, (work.FailureTelemetry()).Value,
                null);
        }
    }

    private void AdmitRead(DecodedGrainRequest request, CapabilityWorkLifetime work, IResourceLifetimeWorkBinding originalBinding)
    {
        work.Token.ThrowIfCancellationRequested();
        work.RequestId = request.Envelope.RequestId;
        if (request.Envelope.ReadKind is null)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        GrainIdentityContext.Validate(request.Envelope, work.RequestId);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.Admit(requestWorkOwner, work.RequestId, RequestWorkKind.ReadCapability, originalBinding);
    }

    private async Task<GrainOperationReply> ExecuteReadAsync(DecodedGrainRequest request,
        CapabilityWorkLifetime work)
    {
        ValidateFreshRequest(codec, request, work.RequestId, work.Token);
        work.Stage = GrainFailureStage.CapabilityExecution;
        work.BeginPhase(DatabasePhaseKind.AuthorizedReadCapability);
        var result = await capabilities.ReadAsync(request, work.RequestId, work, work.Token);
        work.CompletePhase();
        work.Stage = GrainFailureStage.ReplyEncoding;
        return new GrainOperationReply { Result = result };
    }

    internal static void ValidateFreshRequest(GrainRequestCodec codec, DecodedGrainRequest request,
        Guid requestId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        codec.ValidateScope(request.Envelope);
        GrainIdentityContext.Validate(request.Envelope, requestId);
    }

}
