using System.Collections.Immutable;
using System.Security.Cryptography;

namespace KeyLoad.Clustering;

internal static class AnnProjectionJobDispatchSettlement
{
    private const int NoFailures = 0;

    internal static async Task CompleteAsync(NativeRequestWorkChildReservation? reservation,
        GrainRequestIdentityScope? identity, CancellationTokenSource? cancellation, byte[]? payload, List<Exception> failures)
    {
        var original = failures.Count == NoFailures ? null : failures[NoFailures];
        var cleanup = new List<Exception>();
        Capture(() => identity?.Dispose(), cleanup);
        Capture(() => cancellation?.Dispose(), cleanup);
        Capture(() =>
        {
            if (payload is not null)
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }, cleanup);
        failures.AddRange(cleanup);
        if (reservation is null)
        {
            return;
        }
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

    private static void Capture(Action action, List<Exception> failures)
    {
        try
        {
            action();
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
