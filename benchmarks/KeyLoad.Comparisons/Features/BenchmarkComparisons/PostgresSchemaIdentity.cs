using System.Buffers.Binary;

namespace KeyLoad.Comparisons.Targets;

internal readonly record struct PostgresSchemaIdentity(Guid RunGuid, long LockKey, string Name)
{
    private const string SchemaPrefix = "bench_";
    private const string GuidNameFormat = "N";
    private const int GuidByteCount = 16;

    internal static PostgresSchemaIdentity FromRunId(string runId)
    {
        var runGuid = Guid.Parse(runId);
        Span<byte> bytes = stackalloc byte[GuidByteCount];
        runGuid.TryWriteBytes(bytes, bigEndian: true, out _);
        return new(runGuid, BinaryPrimitives.ReadInt64BigEndian(bytes), SchemaPrefix + runGuid.ToString(GuidNameFormat));
    }
}
