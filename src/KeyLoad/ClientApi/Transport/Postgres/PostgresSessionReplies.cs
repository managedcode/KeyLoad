using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace KeyLoad.Server;

internal static class PostgresSessionReplies
{
    internal static void Parameter(PostgresWireWriter writer, string name, string value)
        => writer.Write(PostgresWireWriter.ParameterStatus, Encoding.UTF8.GetBytes(name + PostgresProtocol.Terminator + value + PostgresProtocol.Terminator));

    internal static void Backend(PostgresWireWriter writer, PostgresCancellationRegistry.Registration backend)
    {
        Span<byte> payload = stackalloc byte[sizeof(int) + sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(payload, backend.Id);
        backend.Secret.AsSpan().CopyTo(payload[sizeof(int)..]);
        writer.Write(PostgresWireWriter.BackendKeyData, payload);
    }

    internal static void Parameters(PostgresWireWriter writer, PostgresPreparedStatement statement)
    {
        var bytes = new byte[sizeof(short) + statement.ParameterOids.Length * sizeof(int)];
        BinaryPrimitives.WriteInt16BigEndian(bytes, checked((short)statement.ParameterOids.Length));
        for (var index = PostgresProtocol.AllRows; index < statement.ParameterOids.Length; index++)
        {
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(sizeof(short) + index * sizeof(int)), statement.ParameterOids[index]);
        }
        writer.Write(ParameterDescription, bytes);
    }

    internal static async Task ResultAsync(PostgresWireWriter writer, PostgresEncodedReply reply, short format,
        PostgresSessionOptions options, int chunkBytes, bool describe, CancellationToken closed)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using (reply)
            {
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    try
                    {
                        await WriteResultAsync(writer, reply.Payload, format, options, chunkBytes, describe, closed);
                    }
                    catch (Exception failure) when (reply.PossibleWrite && global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
                    {
                        throw Errors.Fail(ErrorCode.UnknownWriteOutcome, PostgresProtocol.InterruptedReply, failure);
                    }
                }, failures);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task WriteResultAsync(PostgresWireWriter writer, byte[] json, short format,
        PostgresSessionOptions options, int chunkBytes, bool describe, CancellationToken closed)
    {
        const int RowHeaderBytes = sizeof(short) + sizeof(int);
        var payloadBytes = checked(RowHeaderBytes + json.Length
            + (format == PostgresProtocol.BinaryFormat ? sizeof(byte) : PostgresProtocol.AllRows));
        if (payloadBytes + sizeof(int) > options.MaximumFrameBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, PostgresProtocol.Invalid);
        }
        if (describe)
        {
            writer.Describe(ResultColumn, format);
        }
        writer.Header(PostgresWireWriter.DataRow, payloadBytes);
        var header = new byte[RowHeaderBytes + (format == PostgresProtocol.BinaryFormat ? sizeof(byte) : PostgresProtocol.AllRows)];
        BinaryPrimitives.WriteInt16BigEndian(header, OneColumn);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(sizeof(short)), payloadBytes - RowHeaderBytes);
        if (format == PostgresProtocol.BinaryFormat)
        {
            header[RowHeaderBytes] = PostgresProtocol.JsonbVersion;
        }
        writer.Raw(header);
        for (var offset = PostgresProtocol.AllRows; offset < json.Length;)
        {
            closed.ThrowIfCancellationRequested();
            var count = Math.Min(chunkBytes, json.Length - offset);
            writer.Raw(json.AsSpan(offset, count));
            await writer.FlushAsync(closed);
            offset += count;
        }
        writer.Text(PostgresWireWriter.CommandComplete, ResultTag + PostgresProtocol.CommandSeparator + OneColumn.ToString(CultureInfo.InvariantCulture));
        await writer.FlushAsync(closed);
    }

    internal const byte ParameterDescription = (byte)'t';
    internal const short OneColumn = 1;
    internal const string ResultColumn = "keyload_result";
    internal const string ResultTag = "SELECT";
}
