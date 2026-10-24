using Orleans.Serialization.Buffers;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class EncodedTypeFrameFixture
{
    private const string Tenant = "framing-tenant";
    private const string Database = "framing-database";
    private const string Domain = "framing-domain";
    private const string PartitionKey = "framing-partition";
    private const string Collection = "framing-records";
    private const string DocumentId = "framing-document";
    private const string DocumentJson = "{\"value\":\"界λ\",\"count\":7}";
    private const long ExpectedRevision = 3;
    private const long StoredMessages = 2;
    private const long InFlightMessages = 3;
    private const long InFlightBytes = 128;
    private const long StoredBytes = 512;
    private const long NextReadySequence = 6;
    internal const int FrameVersionIndex = 0;
    internal const byte UnsupportedVersion = 0;
    private const long NoUnreadBytes = 0;
    private static readonly Guid CommandId = Guid.Parse("6144bd58-0876-4dc3-8996-37c9d19d10f6");

    internal static CommandRequest Command() => new(CommandId,
        new(Tenant, Database, Domain, PartitionKey),
        [new PutDocument(Collection, DocumentId, DocumentJson, ExpectedRevision)]);

    internal static QueueCounters Counters() => new(StoredMessages, StoredBytes, InFlightMessages, InFlightBytes, NextReadySequence);

    internal static byte[] Encode<T>()
    {
        using var session = OrleansSerialization.Serializer.SessionPool.GetSession();
        var writer = Writer.CreatePooled(session);
        try
        {
            session.TypeCodec.WriteEncodedType(ref writer, typeof(T));
            writer.Commit();
            return writer.Output.ToArray();
        }
        finally
        {
            writer.Dispose();
        }
    }

    internal static (Type? Type, long Remaining) Decode<T>(byte[] bytes)
    {
        using var session = OrleansSerialization.Serializer.SessionPool.GetSession();
        var reader = Reader.Create(bytes.AsSpan(), session);
        var actual = session.TypeCodec.TryRead(ref reader);
        return (actual, reader.Remaining);
    }

    internal static async Task RequireAsync<T>(T literal)
    {
        var frame = Encode<T>();
        var decoded = Decode<T>(frame);
        await Assert.That(decoded.Type).IsEqualTo(typeof(T));
        await Assert.That(decoded.Remaining).IsEqualTo(NoUnreadBytes);
        await Assert.That(EncodedTypeMeasure.Measure<T>()).IsEqualTo(frame.LongLength);
        await Assert.That(EncodedTypeMeasure.Measure<T>()).IsEqualTo(frame.LongLength);
        await Assert.That(Encode<T>()).IsEquivalentTo(frame, CollectionOrdering.Matching);
        var actual = OrleansSerialization.Deserialize<T>(OrleansSerialization.Serialize(literal));
        await Assert.That(JsonDefaults.Serialize(actual))
            .IsEquivalentTo(JsonDefaults.Serialize(literal), CollectionOrdering.Matching);
    }
}
