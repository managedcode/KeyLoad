using System.Buffers.Binary;
using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresDecimalClusterFrames
{
    internal static byte[] Parse()
    {
        var text = Encoding.UTF8.GetBytes(PostgresDecimalClusterProtocol.Statement + PostgresDecimalClusterProtocol.Terminator
            + PostgresDecimalClusterProtocol.DriverSql + PostgresDecimalClusterProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) + sizeof(int)];
        text.CopyTo(bytes, default(int));
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(text.Length), PostgresDecimalClusterProtocol.One);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(text.Length + sizeof(short)), PostgresDecimalClusterProtocol.NumericOid);
        return bytes;
    }

    internal static byte[] Bind(short format, byte[] value)
    {
        var text = Encoding.UTF8.GetBytes(PostgresDecimalClusterProtocol.Portal + PostgresDecimalClusterProtocol.Terminator
            + PostgresDecimalClusterProtocol.Statement + PostgresDecimalClusterProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) * 4 + sizeof(int) + value.Length];
        text.CopyTo(bytes, default(int));
        var offset = text.Length;
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresDecimalClusterProtocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), format);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresDecimalClusterProtocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), value.Length);
        offset += sizeof(int);
        value.CopyTo(bytes, offset);
        return bytes;
    }

    internal static byte[] Numeric(short weight, ushort sign, short scale, params short[] digits)
    {
        var bytes = new byte[PostgresDecimalClusterProtocol.HeaderBytes + digits.Length * sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(bytes, checked((short)digits.Length));
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(sizeof(short)), weight);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(sizeof(short) * 2), sign);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(sizeof(short) * 3), scale);
        for (var index = 0; index < digits.Length; index++)
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(PostgresDecimalClusterProtocol.HeaderBytes + index * sizeof(short)), digits[index]);
        }
        return bytes;
    }
}
