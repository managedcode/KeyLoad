using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Serialization;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;
using Orleans.Serialization.WireProtocol;

namespace KeyLoad.UnitTests;

/// <summary>AC-IS-005: genuine native codecs validate admission shape without decoding opaque content.</summary>
internal sealed class ReplicaNativeInspectionTests
{
    private const string Voter = "voter-a";
    private const string Principal = "principal-Україна-🙂";
    private const string BooleanPayload = " true ";
    private const int MaximumEntries = 2;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeSharedStringAndOperationReferencesRetainExactBatchMeasure(bool sharedOperation)
    {
        using var database = new TestDatabase();
        var operation = database.Database.NormalizeOperation(new(Guid.NewGuid(), OperationKind.SetDispatch, Principal,
            DateTimeOffset.UnixEpoch, BooleanPayload));
        var copy = sharedOperation ? operation : operation with { };
        ImmutableArray<ReplicaEntry> entries = [new(1, 1, operation), new(2, 1, copy)];
        var request = new AppendRequest(Voter, 1, 0, 0, 0, entries);
        var inspected = ReplicaNativeInspection.Inspect<AppendRequest>(ReplicaProtocolCodec.Serialize(request), MaximumEntries);
        var first = inspected.Value.Entries[0].Operation!;
        var second = inspected.Value.Entries[1].Operation!;
        await Assert.That(inspected.Metadata(inspected.Value.LeaderId, Encoding.UTF8.GetByteCount(Voter))).IsEqualTo(Voter);
        await Assert.That(inspected.Metadata(first.PrincipalId, Encoding.UTF8.GetByteCount(Principal))).IsEqualTo(Principal);
        await Assert.That(inspected.Utf8Length(first.PayloadJson)).IsEqualTo(Encoding.UTF8.GetByteCount(BooleanPayload));
        await Assert.That(ReferenceEquals(first.PayloadJson, second.PayloadJson)).IsTrue();
        await Assert.That(first.NativePayload.Span.SequenceEqual(operation.NativePayload.Span)).IsTrue();
        await Assert.That(inspected.MeasureEntries(inspected.Value.Entries)).IsEqualTo(ReplicaProtocolCodec.MeasureEntries(entries));
        await Assert.That(inspected.MeasureEntries(inspected.Value.Entries))
            .IsEqualTo(ReplicaProtocolCodec.SerializeEntries(entries).LongLength);
    }

    [Test]
    public async Task NativeHeartbeatAndReadBarrierAreInitializedAndEmpty()
    {
        var heartbeat = new AppendRequest(Voter, 1, 0, 0, 0, []);
        var inspected = ReplicaNativeInspection.Inspect<AppendRequest>(ReplicaProtocolCodec.Serialize(heartbeat), MaximumEntries);
        await Assert.That(inspected.Value.Entries.IsDefault).IsFalse();
        await Assert.That(inspected.Value.Entries.Length).IsEqualTo(0);
        var read = ReplicaNativeInspection.Inspect<string>(ReplicaProtocolCodec.Serialize(string.Empty), MaximumEntries);
        await Assert.That(read.Utf8Length(read.Value)).IsEqualTo(0);
    }

    [Test]
    [Arguments(ReplicaMalformedVoteShape.Duplicate)]
    [Arguments(ReplicaMalformedVoteShape.Missing)]
    [Arguments(ReplicaMalformedVoteShape.Unknown)]
    [Arguments(ReplicaMalformedVoteShape.WrongType)]
    public async Task NativeMalformedVoteShapeIsRejected(ReplicaMalformedVoteShape shape)
    {
        var bytes = ReplicaNativeFixtureWriter.Vote(shape, new(Voter, 1, 0, 0));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<VoteRequest>(bytes, MaximumEntries));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    [Arguments(ReplicaMalformedArrayShape.Underfilled)]
    [Arguments(ReplicaMalformedArrayShape.Overfilled)]
    [Arguments(ReplicaMalformedArrayShape.Null)]
    public async Task NativeDeclaredEntryArrayMustMatchActualElements(ReplicaMalformedArrayShape shape)
    {
        var bytes = ReplicaNativeFixtureWriter.Batch(shape, new([new(1, 1, null), new(2, 1, null)]));
        var error = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicaEntryBatch>(bytes, MaximumEntries));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task NativeWrongDynamicRootAndEntryCountAboveExistingLimitFailBeforeFullDecode()
    {
        var wrong = ReplicaProtocolCodec.Serialize(new AppendReply(1, true, 1, 2));
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<VoteRequest>(wrong, MaximumEntries)).Code)
            .IsEqualTo(ErrorCode.Corruption);
        var entries = new ReplicaEntryBatch([new(1, 1, null), new(2, 1, null)]);
        var encoded = ReplicaProtocolCodec.Serialize(entries);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => ReplicaNativeInspection.Inspect<ReplicaEntryBatch>(encoded, 1)).Code)
            .IsEqualTo(ErrorCode.Corruption);
    }
}

internal enum ReplicaMalformedVoteShape { Duplicate, Missing, Unknown, WrongType }
internal enum ReplicaMalformedArrayShape { Underfilled, Overfilled, Null }

/// <summary>Malformed fixtures are authored by native codecs inside the authoritative shared envelope.</summary>
internal static class ReplicaNativeFixtureWriter
{
    internal static byte[] Vote(ReplicaMalformedVoteShape shape, VoteRequest value)
        => Encode(value, new ReplicaMalformedVoteCodec(shape));

    internal static byte[] Batch(ReplicaMalformedArrayShape shape, ReplicaEntryBatch value)
        => Encode(value, new ReplicaMalformedBatchCodec(shape));

    internal static byte[] State(ReplicaMalformedVoteShape shape, ReplicaHardState value)
        => Encode(value, new ReplicaMalformedHardStateCodec(shape));

    internal static byte[] Encode<T, TCodec>(T value, TCodec codec) where TCodec : class
    {
        var context = NativeSerializerProviders.CreateInspection(typeof(T), builder =>
        {
            builder.AddAssembly(typeof(ReplicaEntry).Assembly);
            builder.Services.AddSingleton(codec);
            builder.Configure(options => options.FieldCodecs.Add(typeof(TCodec)));
        });
        var body = context.Serializer.SerializeToArray(new NativePayload { Version = NativePayloadVersion.Current, Value = value });
        var bytes = new byte[checked(ReplicaProtocol.PayloadPrefixBytes + body.Length)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ReplicaProtocol.PayloadMagic);
        body.CopyTo(bytes, ReplicaProtocol.PayloadPrefixBytes);
        return bytes;
    }
}

internal sealed class ReplicaMalformedHardStateCodec(ReplicaMalformedVoteShape shape) : IFieldCodec<ReplicaHardState>
{
    public ReplicaHardState ReadValue<TInput>(ref Reader<TInput> reader, Field field)
        => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint fieldIdDelta,
        [AllowNull] Type expectedType, [AllowNull] ReplicaHardState value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, fieldIdDelta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(fieldIdDelta, expectedType, typeof(ReplicaHardState), WireType.TagDelimited);
        writer.WriteEndBase();
        writer.Session.CodecProvider.GetCodec<int>().WriteField(ref writer, 0, typeof(int), value.Version);
        if (shape == ReplicaMalformedVoteShape.Duplicate)
        {
            writer.Session.CodecProvider.GetCodec<int>().WriteField(ref writer, 0, typeof(int), value.Version);
        }
        writer.Session.CodecProvider.GetCodec<Guid>().WriteField(ref writer, 1, typeof(Guid), value.Incarnation);
        if (shape == ReplicaMalformedVoteShape.WrongType)
        {
            StringCodec.WriteField(ref writer, 1, value.VotedFor);
        }
        else if (shape != ReplicaMalformedVoteShape.Missing)
        {
            Int64Codec.WriteField(ref writer, 1, value.Term);
        }
        StringCodec.WriteField(ref writer, shape == ReplicaMalformedVoteShape.Missing ? 2U : 1U, value.VotedFor);
        Int64Codec.WriteField(ref writer, 1, value.LastIndex);
        Int64Codec.WriteField(ref writer, 1, value.CommittedIndex);
        writer.Session.CodecProvider.GetCodec<ReplicaSnapshot>().WriteField(ref writer, 1, typeof(ReplicaSnapshot), value.Snapshot!);
        if (shape == ReplicaMalformedVoteShape.Unknown)
        {
            BoolCodec.WriteField(ref writer, 1, true);
        }
        writer.WriteEndObject();
    }
}

// These real native writers intentionally emit rejected shape; no transport/store service double is used.
internal sealed class ReplicaMalformedVoteCodec(ReplicaMalformedVoteShape shape) : IFieldCodec<VoteRequest>
{
    public VoteRequest ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] VoteRequest value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(VoteRequest), WireType.TagDelimited);
        writer.WriteEndBase();
        StringCodec.WriteField(ref writer, 0, value.CandidateId);
        if (shape == ReplicaMalformedVoteShape.Duplicate)
        { StringCodec.WriteField(ref writer, 0, value.CandidateId); }
        if (shape == ReplicaMalformedVoteShape.WrongType)
        { StringCodec.WriteField(ref writer, 1, value.CandidateId); }
        else
        { Int64Codec.WriteField(ref writer, 1, value.Term); }
        Int64Codec.WriteField(ref writer, 1, value.LastIndex);
        if (shape != ReplicaMalformedVoteShape.Missing)
        { Int64Codec.WriteField(ref writer, 1, value.LastTerm); }
        if (shape == ReplicaMalformedVoteShape.Unknown)
        { BoolCodec.WriteField(ref writer, 1, true); }
        writer.WriteEndObject();
    }
}

internal sealed class ReplicaMalformedBatchCodec(ReplicaMalformedArrayShape shape) : IFieldCodec<ReplicaEntryBatch>
{
    public ReplicaEntryBatch ReadValue<TInput>(ref Reader<TInput> reader, Field field) => throw new NotSupportedException();

    public void WriteField<TBufferWriter>(ref Writer<TBufferWriter> writer, uint delta,
        [AllowNull] Type expectedType, [AllowNull] ReplicaEntryBatch value) where TBufferWriter : IBufferWriter<byte>
    {
        if (ReferenceCodec.TryWriteReferenceField(ref writer, delta, expectedType, value))
        { return; }
        writer.WriteFieldHeader(delta, expectedType, typeof(ReplicaEntryBatch), WireType.TagDelimited);
        writer.WriteEndBase();
        ReferenceCodec.MarkValueField(writer.Session);
        writer.WriteFieldHeader(0, typeof(ImmutableArray<ReplicaEntry>), typeof(ImmutableArray<ReplicaEntry>), WireType.TagDelimited);
        if (shape == ReplicaMalformedArrayShape.Null)
        {
            writer.Session.CodecProvider.GetCodec<ReplicaEntry[]>().WriteField(ref writer, 0, typeof(ReplicaEntry[]), null!);
        }
        else
        {
            WriteArray(ref writer, value.Entries);
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private void WriteArray<TBufferWriter>(ref Writer<TBufferWriter> writer, ImmutableArray<ReplicaEntry> entries)
        where TBufferWriter : IBufferWriter<byte>
    {
        var values = entries.ToArray();
        if (ReferenceCodec.TryWriteReferenceField(ref writer, 0, typeof(ReplicaEntry[]), values))
        { return; }
        writer.WriteFieldHeader(0, typeof(ReplicaEntry[]), typeof(ReplicaEntry[]), WireType.TagDelimited);
        var declared = shape == ReplicaMalformedArrayShape.Underfilled ? values.Length : values.Length - 1;
        UInt32Codec.WriteField(ref writer, 0, checked((uint)declared));
        var actual = shape == ReplicaMalformedArrayShape.Underfilled ? values.Length - 1 : values.Length;
        var element = writer.Session.CodecProvider.GetCodec<ReplicaEntry>();
        for (var index = 0; index < actual; index++)
        {
            element.WriteField(ref writer, index == 0 ? 1U : 0U, typeof(ReplicaEntry), values[index]);
        }
        writer.WriteEndObject();
    }
}
