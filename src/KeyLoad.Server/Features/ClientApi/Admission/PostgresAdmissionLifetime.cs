using Microsoft.AspNetCore.Connections;

namespace KeyLoad.Server;

internal sealed class PostgresAdmissionLifetime
{
    private CancellationTokenRegistration expiry;
    internal CancellationToken Token { get; private init; }
    internal bool Completed { get; private set; }

    internal static async Task RunAsync(ConnectionContext context, ConnectionDelegate next,
        TimeSpan duration, TimeProvider clock)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var deadline = new PostgresTransportDeadline(duration, clock, context.ConnectionClosed);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var owner = context.Features.Get<ServerConnectionFeature>()
                    ?? throw Errors.Fail(ErrorCode.OwnershipLost, ServerConnectionProtocol.Unavailable);
                var lifetime = new PostgresAdmissionLifetime { Token = deadline.Token };
                context.Features.Set(lifetime);
                lifetime.expiry = deadline.Token.Register(static value =>
                    ((ServerConnectionFeature)value!).RequestClose(), owner);
                await ServerFailureObserver.ObserveAsync(() => next(context), failures).ConfigureAwait(false);
                await ServerFailureObserver.ObserveAsync(() => lifetime.expiry.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
                ServerFailureObserver.Observe(() => context.Features.Set<PostgresAdmissionLifetime>(null), failures);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal async Task CompleteAsync()
    {
        Token.ThrowIfCancellationRequested();
        await expiry.DisposeAsync().ConfigureAwait(false);
        Token.ThrowIfCancellationRequested();
        Completed = true;
    }

    internal static PostgresAdmissionLifetime Require(ConnectionContext context)
        => context.Features.Get<PostgresAdmissionLifetime>()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ServerConnectionProtocol.Unavailable);
}
