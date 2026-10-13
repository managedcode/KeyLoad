using System.Buffers.Binary;
using System.Text;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPortalOwnershipFrames
{
    internal static byte[] Parse(string name, string sql)
    {
        var text = Encoding.UTF8.GetBytes(name + PostgresPortalOwnershipProtocol.Terminator + sql
            + PostgresPortalOwnershipProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short)];
        text.CopyTo(bytes, default(int));
        return bytes;
    }

    internal static byte[] Bind(string name, string statement)
    {
        var text = Encoding.UTF8.GetBytes(name + PostgresPortalOwnershipProtocol.Terminator + statement
            + PostgresPortalOwnershipProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(short) + sizeof(short) + sizeof(short)];
        text.CopyTo(bytes, default(int));
        return bytes;
    }

    internal static byte[] Target(byte kind, string name)
        => [(byte)kind, .. Encoding.UTF8.GetBytes(name + PostgresPortalOwnershipProtocol.Terminator)];

    internal static byte[] Execute(string name)
    {
        var text = Encoding.UTF8.GetBytes(name + PostgresPortalOwnershipProtocol.Terminator);
        var bytes = new byte[text.Length + sizeof(int)];
        text.CopyTo(bytes, default(int));
        return bytes;
    }

    internal static byte[] Description()
    {
        var name = Encoding.UTF8.GetBytes(PostgresRf3Protocol.ResultColumn + PostgresPortalOwnershipProtocol.Terminator);
        var bytes = new byte[sizeof(short) + name.Length + sizeof(int) + sizeof(short)
            + sizeof(int) + sizeof(short) + sizeof(int) + sizeof(short)];
        BinaryPrimitives.WriteInt16BigEndian(bytes, PostgresRf3Protocol.OneColumn);
        name.CopyTo(bytes, sizeof(short));
        var offset = sizeof(short) + name.Length + sizeof(int) + sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), PostgresPortalOwnershipProtocol.JsonbOid);
        offset += sizeof(int);
        BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(offset), PostgresPortalOwnershipProtocol.UnknownLength);
        offset += sizeof(short);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), PostgresPortalOwnershipProtocol.UnknownLength);
        return bytes;
    }
}
