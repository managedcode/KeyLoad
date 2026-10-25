using System.Buffers;
using System.Buffers.Binary;
using Microsoft.AspNetCore.Connections;

namespace KeyLoad.Server;

internal sealed class PostgresSslNegotiation(global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured, PostgresRetainedAdmission admission)
{
    private PostgresSessionOptions Options => configured.Value;
    private const int SslRequestBytes = sizeof(int) * 2;

    internal static async Task RunTcpAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var token = PostgresAdmissionLifetime.Require(context).Token;
        var input = context.Transport.Input;
        var read = await input.ReadAtLeastAsync(SslRequestBytes, token);
        var requested = IsSslRequest(read.Buffer);
        var consumed = requested ? read.Buffer.GetPosition(SslRequestBytes) : read.Buffer.Start;
        input.AdvanceTo(consumed, consumed);
        if (read.IsCanceled)
        {
            token.ThrowIfCancellationRequested();
            throw Errors.Fail(ErrorCode.Cancelled, PostgresProtocol.Invalid);
        }
        if (requested)
        {
            context.Transport.Output.Write(SslRejected);
            var flushed = await context.Transport.Output.FlushAsync(token);
            if (flushed.IsCompleted || flushed.IsCanceled)
            {
                token.ThrowIfCancellationRequested();
                throw new EndOfStreamException();
            }
        }
        await next(context);
    }

    private static bool IsSslRequest(ReadOnlySequence<byte> buffer)
    {
        if (buffer.Length < SslRequestBytes)
        {
            return false;
        }
        Span<byte> header = stackalloc byte[SslRequestBytes];
        buffer.Slice(PostgresProtocol.FirstField, SslRequestBytes).CopyTo(header);
        return BinaryPrimitives.ReadInt32BigEndian(header) == SslRequestBytes
            && BinaryPrimitives.ReadInt32BigEndian(header[sizeof(int)..]) == PostgresProtocol.SslRequest;
    }
    internal async Task RunAsync(ConnectionContext context, ConnectionDelegate next)
    {
        var token = PostgresAdmissionLifetime.Require(context).Token;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var frame = (await PostgresFrameReader.ReadAsync(context.Transport.Input, true,
                Options.MaximumStartupBytes, admission, token)).Value;
            await ServerFailureObserver.ObserveAsync(() =>
            {
                if (frame is null || !RequireSsl(frame.Payload))
                {
                    throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential);
                }
                return Task.CompletedTask;
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        context.Transport.Output.Write(SslAccepted);
        var flushed = await context.Transport.Output.FlushAsync(token);
        if (flushed.IsCompleted || flushed.IsCanceled)
        {
            token.ThrowIfCancellationRequested();
            throw new EndOfStreamException();
        }
        await next(context);
    }

    private static bool RequireSsl(ReadOnlySpan<byte> payload)
    {
        var reader = new PostgresPayloadReader(payload);
        var code = reader.Int32();
        reader.Complete();
        return code == PostgresProtocol.SslRequest;
    }

    private static ReadOnlySpan<byte> SslAccepted => [PostgresProtocol.SslAccepted];
    private static ReadOnlySpan<byte> SslRejected => [PostgresProtocol.SslRejected];
}
