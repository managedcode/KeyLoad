using System.Buffers.Binary;
using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresInt16ClusterFrames
{
    internal static byte[] Parse()
    {
        var text = Encoding.UTF8.GetBytes(PostgresInt16ClusterProtocol.Statement + PostgresInt16ClusterProtocol.Terminator
            + PostgresInt16ClusterProtocol.DriverSql + PostgresInt16ClusterProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) + sizeof(int)];
        text.CopyTo(bytes, default(int));
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(text.Length), PostgresInt16ClusterProtocol.One);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(text.Length + sizeof(short)), PostgresInt16ClusterProtocol.Int16Oid);
        return bytes;
    }

    internal static byte[] Bind(short format, byte[] value)
    {
        var text = Encoding.UTF8.GetBytes(PostgresInt16ClusterProtocol.Portal + PostgresInt16ClusterProtocol.Terminator
            + PostgresInt16ClusterProtocol.Statement + PostgresInt16ClusterProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) * 4 + sizeof(int) + value.Length];
        text.CopyTo(bytes, default(int));
        var offset = text.Length;
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresInt16ClusterProtocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), format);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresInt16ClusterProtocol.One);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), value.Length);
        offset += sizeof(int);
        value.CopyTo(bytes, offset);
        return bytes;
    }

}
