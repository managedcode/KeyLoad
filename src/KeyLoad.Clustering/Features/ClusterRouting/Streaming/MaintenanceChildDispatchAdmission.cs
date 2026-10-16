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
    private bool cleanupPrepared;
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

    internal async Task CompleteAsync(List<Exception> failures)
    {
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        var cleanup = new List<Exception>();
        cleanupPrepared = true;
        Dispose(cleanup);
        failures.AddRange(cleanup);
        if (reservation is not null)
        {
            try
            {
                _ = await reservation.CompleteDispatchAsync(original, cleanup.ToImmutableArray()).ConfigureAwait(true);
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
        if (!cleanupPrepared)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, GrainRoutingProtocol.InvalidRequest);
        }
        var failures = new List<Exception>();
        Dispose(failures);
        Exception? failure = null;
        foreach (var error in failures)
        {
            failure = NativeRequestWorkSettlement.Preserve(failure, error);
        }
        NativeRequestWorkSettlement.Rethrow(failure);
    }

    private void Dispose(List<Exception> cleanup)
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        if (identity is { } originalIdentity)
        {
            Capture(originalIdentity.Dispose, cleanup);
        }
        if (cancellation is { } originalCancellation)
        {
            Capture(originalCancellation.Dispose, cleanup);
        }
        if (ownedPayload is not null)
        {
            Capture(() => CryptographicOperations.ZeroMemory(ownedPayload), cleanup);
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
