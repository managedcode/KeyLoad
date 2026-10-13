using System.Buffers.Binary;
using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16Rf3Frames
{
    internal static byte[] Parse()
    {
        var text = Encoding.UTF8.GetBytes(PostgresInt16Rf3Protocol.Statement + PostgresInt16Rf3Protocol.Terminator
            + PostgresInt16Rf3Protocol.DriverSql + PostgresInt16Rf3Protocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) + sizeof(int)];
        text.CopyTo(bytes, default(int));
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(text.Length), PostgresInt16Rf3Protocol.One);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(text.Length + sizeof(short)), PostgresInt16Rf3Protocol.Int16Oid);
        return bytes;
    }

    internal static byte[] Bind(short format, byte[] value)
    {
        var text = Encoding.UTF8.GetBytes(PostgresInt16Rf3Protocol.Portal + PostgresInt16Rf3Protocol.Terminator
            + PostgresInt16Rf3Protocol.Statement + PostgresInt16Rf3Protocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) * 4 + sizeof(int) + value.Length];
        text.CopyTo(bytes, default(int));
        var offset = text.Length;
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresInt16Rf3Protocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), format);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresInt16Rf3Protocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), value.Length);
        offset += sizeof(int);
        value.CopyTo(bytes, offset);
        return bytes;
    }

}
