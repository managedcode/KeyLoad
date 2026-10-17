using System.Buffers;
using Microsoft.AspNetCore.Connections;

namespace KeyLoad.Server;

internal sealed class PostgresSslNegotiation(global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured, PostgresRetainedAdmission admission)
{
    private PostgresSessionOptions Options => configured.Value;
    internal async Task RunAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var token = PostgresAdmissionLifetime.Require(context).Token;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var frame = await PostgresFrameReader.ReadAsync(context.Transport.Input, true,
                Options.MaximumStartupBytes, admission, token).ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() =>
            {
                if (frame is null || !RequireSsl(frame.Payload))
                {
                    throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
                }
                return Task.CompletedTask;
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        context.Transport.Output.Write(SslAccepted);
        var flushed = await context.Transport.Output.FlushAsync(token).ConfigureAwait(false);
        if (flushed.IsCompleted || flushed.IsCanceled)
        {
            token.ThrowIfCancellationRequested();
            throw new EndOfStreamException();
        }
        await next(context).ConfigureAwait(false);
    }

    private static bool RequireSsl(ReadOnlySpan<byte> payload)
    {
        var reader = new PostgresPayloadReader(payload);
        var code = reader.Int32();
        reader.Complete();
        return code == PostgresProtocol.SslRequest;
    }

    private static ReadOnlySpan<byte> SslAccepted => [PostgresProtocol.SslAccepted];
}
