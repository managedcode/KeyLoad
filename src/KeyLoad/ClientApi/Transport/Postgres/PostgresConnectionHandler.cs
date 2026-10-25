using KeyLoad.Clustering;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresConnectionHandler(IServiceProvider services, IOptions<PostgresSessionOptions> configured,
    PostgresRetainedAdmission admission, PostgresCancellationRegistry cancellations) : ConnectionHandler
{
    public override async Task OnConnectedAsync(ConnectionContext context)
    {
        var options = configured.Value;
        if (options.RequireTls && context.Features.Get<ITlsHandshakeFeature>() is null
            || context.Features.Get<ServerConnectionFeature>() is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
        }
        try
        {
            var request = await ReadStartupAsync(context, options);
            if (request is null)
            {
                return;
            }
            if (request.Cancel is { } cancel)
            {
                await cancellations.CancelAsync(cancel.Id, cancel.Secret);
                return;
            }
            await new PostgresProtocolSession(services, configured, admission, cancellations)
                .RunAsync(context, request.Startup ?? throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid));
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error) && !context.ConnectionClosed.IsCancellationRequested
            && (PostgresAdmissionLifetime.Require(context).Completed || !PostgresAdmissionLifetime.Require(context).Token.IsCancellationRequested))
        {
            var failures = new List<Exception> { error };
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var deadline = new PostgresTransportDeadline(
                    services.GetRequiredService<IOptions<GrainRoutingOptions>>().Value.RequestLifetime,
                    services.GetRequiredService<TimeProvider>(), context.ConnectionClosed);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var writer = new PostgresWireWriter(context.Transport.Output, options.MaximumFrameBytes);
                    PostgresErrorReply.Write(writer, error);
                    await writer.FlushAsync(deadline.Token);
                }, failures);
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private async Task<StartupRequest?> ReadStartupAsync(ConnectionContext context, PostgresSessionOptions options)
    {
        StartupRequest? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var frame = (await PostgresFrameReader.ReadAsync(context.Transport.Input, true,
                options.MaximumStartupBytes, admission, PostgresAdmissionLifetime.Require(context).Token)).Value;
            await ServerFailureObserver.ObserveAsync(() =>
            {
                if (frame is not null)
                {
                    var cancel = (Cancellation(frame.Payload)).Value;
                    result = cancel is not null ? new(null, cancel)
                        : new(PostgresStartupDecoder.Read(frame.Payload, options), null);
                }
                return Task.CompletedTask;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result;
    }

    private static (bool HasValue, (int Id, int Secret)? Value) Cancellation(ReadOnlySpan<byte> payload)
    {
        var reader = new PostgresPayloadReader(payload);
        if (reader.Int32() != PostgresProtocol.CancelRequest)
        {
            return (false, default((int Id, int Secret)?));
        }
        var id = reader.Int32();
        var secret = reader.Int32();
        reader.Complete();
        return (true, (((int Id, int Secret)?)((id, secret))));
    }

    private sealed record StartupRequest(PostgresStartupDecoder.Startup? Startup, (int Id, int Secret)? Cancel);
}
