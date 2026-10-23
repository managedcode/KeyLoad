using System.Security.Cryptography;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal sealed class MaintenanceChildDispatchAdmission(byte[]? ownedPayload) : IDisposable
{
    private CancellationTokenSource? cancellation;
    private GrainRequestIdentityScope? identity;
    private List<Exception>? cleanup;
    private DecodedGrainRequest? observedRequest;
    private IGrainContext? observedContext;
    private GrainRequestPhaseSettlement? observation;
    private bool disposed;
    internal CancellationToken Token => cancellation?.Token
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal void Open(DecodedGrainRequest parent, GrainRequestEnvelope child, Guid actorId, Guid commandId,
        IServiceProvider services, GrainRequestCodec codec,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var verified = codec.DecodeRequest(child, actorId);
        if (verified.Envelope.PrincipalId != parent.Envelope.PrincipalId || verified.Envelope.CommandId != commandId
            || verified.Envelope.ExpiresAt != parent.Envelope.ExpiresAt
            || binding is not IRequestWorkBinding execution
            || !ReferenceEquals(binding.ResultRetention, execution.OriginalProducer.ResultRetention))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, execution.OriginalProducer.Cancellation);
        identity = GrainRequestIdentityScope.ForChild(services, parent.Envelope, actorId, commandId, cancellation.Token);
        if (codec.HasPhaseObserver)
        {
            observedRequest = verified;
            observedContext = services.GetRequiredService<IGrainContextAccessor>().GrainContext;
            observation = new(codec);
            observation.SetIdentity(verified);
        }
    }

    internal ValueTask ObserveStartedAsync(GrainRequestCodec codec)
        => observedRequest is { } request
            ? codec.ObservePhaseAsync(request, GrainRequestPhase.RequestStarted, observedContext, Token)
            : ValueTask.CompletedTask;

    internal async Task JoinObservationAsync(List<Exception> failures)
    {
        if (observation is null)
        {
            return;
        }
        try
        {
            await observation.SettleAsync(observedContext).ConfigureAwait(true);
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    // The sole caller has awaited the actual Task before closing these local scopes.
    internal void PrepareCleanup(List<Exception> failures)
    {
        cleanup = failures;
    }

    internal async Task CancelAsync(List<Exception> failures)
    {
        try
        {
            if (cancellation is not null)
            {
                await cancellation.CancelAsync().ConfigureAwait(true);
            }
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }

    public void Dispose()
    {
        var cleanup = this.cleanup ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        if (disposed)
        {
            return;
        }
        disposed = true;
        DisposeIdentity(cleanup);
        DisposeCancellation(cleanup);
        if (ownedPayload is not null)
        {
            Capture(() => CryptographicOperations.ZeroMemory(ownedPayload), cleanup);
        }
    }

    private void DisposeIdentity(List<Exception> cleanup)
    {
        try
        {
            identity?.Dispose();
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            cleanup.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            cleanup.Add(failure);
        }
    }

    private void DisposeCancellation(List<Exception> cleanup)
    {
        try
        {
            cancellation?.Dispose();
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            cleanup.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            cleanup.Add(failure);
        }
    }

    private static void Capture(Action operation, List<Exception> failures)
    {
        try
        {
            operation();
        }
        catch (Exception failure) when (CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!CqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }
}
