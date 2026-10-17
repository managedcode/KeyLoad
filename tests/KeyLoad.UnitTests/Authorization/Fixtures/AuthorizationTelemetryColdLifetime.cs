using KeyLoad.Server;
using Microsoft.AspNetCore.Builder;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryColdLifetime
{
    internal static async Task JoinAsync(HttpClient? client, WebApplication? app, Action flush,
        CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(flush, failures);
        ServerFailureObserver.Observe(() => client?.Dispose(), failures);
        if (app is { } owned)
        {
            await ServerFailureObserver.ObserveAsync(() => owned.StopAsync(cancellationToken), failures);
            ServerFailureObserver.Observe(((IDisposable)owned).Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
