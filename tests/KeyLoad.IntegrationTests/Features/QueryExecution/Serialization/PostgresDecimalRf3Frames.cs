using System.Buffers.Binary;
using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalRf3Frames
{
    internal static byte[] Parse()
    {
        var text = Encoding.UTF8.GetBytes(PostgresDecimalRf3Protocol.Statement + PostgresDecimalRf3Protocol.Terminator
            + PostgresDecimalRf3Protocol.DriverSql + PostgresDecimalRf3Protocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) + sizeof(int)];
        text.CopyTo(bytes, default(int));
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(text.Length), PostgresDecimalRf3Protocol.One);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(text.Length + sizeof(short)), PostgresDecimalRf3Protocol.NumericOid);
        return bytes;
    }

    internal static byte[] Bind(short format, byte[] value)
    {
        var text = Encoding.UTF8.GetBytes(PostgresDecimalRf3Protocol.Portal + PostgresDecimalRf3Protocol.Terminator
            + PostgresDecimalRf3Protocol.Statement + PostgresDecimalRf3Protocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) * 4 + sizeof(int) + value.Length];
        text.CopyTo(bytes, default(int));
        var offset = text.Length;
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresDecimalRf3Protocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), format);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresDecimalRf3Protocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), value.Length);
        offset += sizeof(int);
        value.CopyTo(bytes, offset);
        return bytes;
    }

    internal static byte[] Numeric(short weight, ushort sign, short scale, params short[] digits)
    {
        var bytes = new byte[PostgresDecimalRf3Protocol.HeaderBytes + digits.Length * sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(bytes, checked((short)digits.Length));
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(sizeof(short)), weight);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(sizeof(short) * 2), sign);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(sizeof(short) * 3), scale);
        for (var index = 0; index < digits.Length; index++)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(PostgresDecimalRf3Protocol.HeaderBytes + index * sizeof(short)), digits[index]);
        }
        return bytes;
    }
}
