using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Replication;

internal sealed class ReplicaInspectedValue<T>(T value, ReplicaInspectionBuffers buffers, NativeSerializerContext context)
{
    internal T Value { get; } = value;
    internal string Metadata(string proxy, int maximumUtf8Bytes) => buffers.Metadata(proxy, maximumUtf8Bytes);
    internal ReadOnlyMemory<byte> Utf8(string proxy) => buffers.Utf8(proxy);
    internal int Utf8Length(string proxy) => buffers.Utf8(proxy).Length;
    internal int Utf16Length(string proxy) => buffers.Utf16Length(proxy);

    internal long MeasureEntries(IReadOnlyList<ReplicaEntry> entries)
    {
        var batch = new ReplicaEntryBatch([.. entries]);
        using var session = context.Sessions.GetSession();
        using var bound = ReplicaInspectionBuffers.Bind(session, buffers.Counter());
        return checked(ReplicaProtocol.PayloadPrefixBytes + NativeSerialization.Measure(batch, session));
    }
}
