using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;

namespace KeyLoad.Server;

internal static class PostgresFrameReader
{
    internal const int LengthBytes = sizeof(int);
    private const int TypedHeaderBytes = LengthBytes + sizeof(byte);

    internal static async ValueTask<PostgresFrame?> ReadAsync(PipeReader input, bool startup,
        int maximumBytes, PostgresRetainedAdmission admission, CancellationToken token)
    {
        var header = new byte[startup ? LengthBytes : TypedHeaderBytes];
        if (!await FillAsync(input, header, allowEmpty: true, token).ConfigureAwait(false))
        {
            return null;
        }
        var size = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(startup ? PostgresProtocol.FirstField : sizeof(byte), LengthBytes));
        if (size < LengthBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        if (size > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PostgresProtocol.Invalid);
        }
        var count = size - LengthBytes;
        admission.Resize(PostgresProtocol.AllRows, count);
        PostgresFrame? frame = null;
        try
        {
            frame = new(startup ? PostgresProtocol.StartupMessage : header[PostgresProtocol.FirstField], new byte[count], admission);
            _ = await FillAsync(input, frame.Payload, allowEmpty: false, token).ConfigureAwait(false);
            return frame;
        }
        catch (Exception primary)
        {
            var failures = new List<Exception> { primary };
            ServerFailureObserver.Observe(() =>
            {
                if (frame is null)
                {
                    admission.Resize(count, PostgresProtocol.AllRows);
                }
                else
                {
                    frame.Dispose();
                }
            }, failures);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }

    private static async ValueTask<bool> FillAsync(PipeReader input, Memory<byte> target,
        bool allowEmpty, CancellationToken token)
    {
        var copied = PostgresProtocol.AllRows;
        while (copied < target.Length)
        {
            var read = await input.ReadAsync(token).ConfigureAwait(false);
            var buffer = read.Buffer;
            if (read.IsCanceled)
            {
                input.AdvanceTo(buffer.Start, buffer.End);
                token.ThrowIfCancellationRequested();
                throw Errors.Fail(ErrorCode.Cancelled, PostgresProtocol.Invalid);
            }
            var count = (int)Math.Min(buffer.Length, target.Length - copied);
            buffer.Slice(PostgresProtocol.FirstField, count).CopyTo(target.Span[copied..]);
            copied += count;
            var end = buffer.GetPosition(count);
            input.AdvanceTo(end, count == buffer.Length ? buffer.End : end);
            if (copied == target.Length)
            {
                return true;
            }
            if (!read.IsCompleted)
            {
                continue;
            }
            if (allowEmpty && copied == PostgresProtocol.AllRows)
            {
                return false;
            }
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        return true;
    }
}
