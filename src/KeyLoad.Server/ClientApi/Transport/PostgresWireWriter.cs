using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Text;

namespace KeyLoad.Server;

internal sealed class PostgresWireWriter(PipeWriter output, int maximumFrameBytes)
{
    internal const byte Authentication = (byte)'R';
    internal const byte ErrorResponse = (byte)'E';
    internal const byte ReadyForQuery = (byte)'Z';
    internal const byte ParameterStatus = (byte)'S';
    internal const byte BackendKeyData = (byte)'K';
    internal const byte ParseComplete = (byte)'1';
    internal const byte BindComplete = (byte)'2';
    internal const byte CloseComplete = (byte)'3';
    internal const byte NoData = (byte)'n';
    internal const byte RowDescription = (byte)'T';
    internal const byte DataRow = (byte)'D';
    internal const byte CommandComplete = (byte)'C';
    internal const byte EmptyQueryResponse = (byte)'I';
    internal const int JsonbOid = 3802;
    private const int TypeAndLengthBytes = sizeof(byte) + sizeof(int);
    private const short OneColumn = 1;
    private const string InvalidReply = "The native SQL reply exceeds its admitted byte grant.";
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal void Write(byte type, ReadOnlySpan<byte> payload)
    {
        var length = checked(payload.Length + sizeof(int));
        if (length > maximumFrameBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidReply);
        }
        var buffer = output.GetSpan(checked(payload.Length + TypeAndLengthBytes));
        buffer[PostgresProtocol.FirstField] = type;
        BinaryPrimitives.WriteInt32BigEndian(buffer.Slice(sizeof(byte), sizeof(int)), length);
        payload.CopyTo(buffer[TypeAndLengthBytes..]);
        output.Advance(payload.Length + TypeAndLengthBytes);
    }

    internal void Header(byte type, int payloadBytes)
    {
        var length = checked(payloadBytes + sizeof(int));
        if (length > maximumFrameBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidReply);
        }
        var bytes = output.GetSpan(TypeAndLengthBytes);
        bytes[PostgresProtocol.FirstField] = type;
        BinaryPrimitives.WriteInt32BigEndian(bytes.Slice(sizeof(byte), sizeof(int)), length);
        output.Advance(TypeAndLengthBytes);
    }

    internal void Raw(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(output.GetSpan(bytes.Length));
        output.Advance(bytes.Length);
    }

    internal void Empty(byte type) => Write(type, ReadOnlySpan<byte>.Empty);

    internal void Integer(byte type, int value)
    {
        Span<byte> payload = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32BigEndian(payload, value);
        Write(type, payload);
    }

    internal void Text(byte type, string value)
    {
        var payload = new byte[checked(Utf8.GetByteCount(value) + sizeof(byte))];
        Utf8.GetBytes(value, payload);
        Write(type, payload);
    }

    internal void Ready() => Write(ReadyForQuery, [PostgresProtocol.IdleStatus]);

    internal void Describe(string column, short format)
    {
        const int DescriptionFieldsBytes = sizeof(int) + sizeof(short) + sizeof(int)
            + sizeof(short) + sizeof(int) + sizeof(short);
        var nameBytes = Utf8.GetByteCount(column);
        var payload = new byte[sizeof(short) + nameBytes + sizeof(byte) + DescriptionFieldsBytes];
        BinaryPrimitives.WriteInt16BigEndian(payload, OneColumn);
        Utf8.GetBytes(column, payload.AsSpan(sizeof(short), nameBytes));
        var offset = sizeof(short) + nameBytes + sizeof(byte);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset), PostgresProtocol.NoTableOid);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(offset), PostgresProtocol.NoTableAttribute);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset), JsonbOid);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(offset), PostgresProtocol.NullLength);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(offset), PostgresProtocol.NullLength);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt16BigEndian(payload.AsSpan(offset), format);
        Write(RowDescription, payload);
    }

    internal async ValueTask FlushAsync(CancellationToken token)
    {
        var result = await output.FlushAsync(token).ConfigureAwait(false);
        if (result.IsCanceled)
        {
            token.ThrowIfCancellationRequested();
            throw Errors.Fail(ErrorCode.Cancelled, PostgresProtocol.Invalid);
        }
        if (result.IsCompleted)
        {
            throw new EndOfStreamException();
        }
    }
}
