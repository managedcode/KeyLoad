using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal sealed class MaintenanceChildDispatchAdmission(byte[]? ownedPayload) : IDisposable
{
    private const int NoFailures = 0;
    private NativeRequestWorkChildReservation? reservation;
    private CancellationTokenSource? cancellation;
    private GrainRequestIdentityScope? identity;
    private List<Exception>? cleanup;
    private Exception? originalFailure;
    private bool disposed;
    internal CancellationToken Token => cancellation?.Token
        ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);

    internal void Open(DecodedGrainRequest parent, string signed, Guid actorId, Guid commandId,
        IServiceProvider services, GrainRequestCodec codec, TimeProvider clock,
        IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        codec.ValidateScope(parent.Envelope);
        GrainIdentityContext.Validate(parent.Envelope, parent.Envelope.RequestId);
        var database = services.GetRequiredService<DatabaseEngine>();
        var principal = GrainRequestAuthority.ReloadForRequest(database, parent.Envelope, clock);
        var verified = codec.VerifyRequest(signed, actorId);
        if (verified.Envelope.PrincipalId != principal.Id || verified.Envelope.CommandId != commandId
            || verified.Envelope.ExpiresAt != parent.Envelope.ExpiresAt
            || binding is not INativeRequestWorkBinding native
            || !ReferenceEquals(binding.ResultRetention, native.OriginalProducer.ResultRetention))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        reservation = native.OriginalProducer.ReserveLocalChild(verified,
            services.GetRequiredService<PhysicalShardRecord>(), services.GetRequiredService<ReplicaSiloDiscoveryState>().Read());
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, reservation.Cancellation);
        identity = new(services, principal, actorId, commandId, cancellation.Token,
            connectionId: NativeConnectionExecutionIdentity.Resolve(services));
    }

    // The sole caller prepares and physically disposes after Drain, then always joins CompleteAsync.
    internal void PrepareCleanup(List<Exception> failures)
    {
        originalFailure = failures.Count == NoFailures ? null : failures[NoFailures];
        cleanup = [];
    }

    internal async Task CompleteAsync(List<Exception> failures)
    {
        var cleanup = this.cleanup ?? throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        failures.AddRange(cleanup);
        if (reservation is not null)
        {
            try
            {
                _ = await reservation.CompleteDispatchAsync(originalFailure, cleanup.ToImmutableArray()).ConfigureAwait(true);
            }
            catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
            catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                failures.Add(failure);
            }
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
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            cleanup.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            cleanup.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
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
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }
}
