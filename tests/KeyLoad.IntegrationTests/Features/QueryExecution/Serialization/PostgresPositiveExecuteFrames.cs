using System.Buffers.Binary;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class PostgresPositiveExecuteFrames
{
    internal static byte[] Execute(string portal, int rows)
    {
        var bytes = PostgresPortalOwnershipFrames.Execute(portal);
        BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(bytes.Length - sizeof(int)), rows);
        return bytes;
    }
}
