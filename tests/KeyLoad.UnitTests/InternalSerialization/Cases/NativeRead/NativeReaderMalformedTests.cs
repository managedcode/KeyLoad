using System.Text;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeReaderMalformedTests
{
    private const string AtomicPartition = "partition";

    [Test]
    public async Task AcIs002EveryTruncatedPrefixIsCodedCorruptionAndSessionsRemainReusable()
    {
        var expected = new DocumentResult(new(new("tenant", "database", "domain", AtomicPartition), "documents", "id"),
            42, "{\"text\":\"Київ🌍\"}", false, []);
        var bytes = NativeSerialization.Serialize(expected);
        for (var length = 0; length < bytes.Length; length++)
        {
            var decode = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<DocumentResult>(bytes.AsSpan(0, length)));
            var syntax = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Validate(bytes.AsSpan(0, length)));
            await Assert.That(decode.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(syntax.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(decode.Message).IsEqualTo(NativePayloadVersion.InvalidPayload);
        }
        var actual = NativeSerialization.Deserialize<DocumentResult>(bytes);
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(actual.Reference).IsEqualTo(expected.Reference);
        NativeSerialization.Validate(bytes);
    }

    [Test]
    public async Task AcIs002CurrentReplicaPrefixWithNoBodyIsCodedCorruption()
    {
        var expected = new VoteRequest("voter", 2, 0, 0);
        var bytes = ReplicaProtocolCodec.Serialize(expected);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => ReplicaProtocolCodec.Deserialize<VoteRequest>(bytes.AsSpan(0, ReplicaProtocol.PayloadPrefixBytes)));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(ReplicaProtocolCodec.Deserialize<VoteRequest>(bytes)).IsEqualTo(expected);
    }

    [Test]
    public async Task AcIs002InvalidUtf8RemainsRejectedBeforeSuccessfulReaderReuse()
    {
        const string value = "Київ🌍";
        var bytes = NativeSerialization.Serialize(value);
        var position = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value));
        await Assert.That(position >= 0).IsTrue();
        bytes[position] = 0xff;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => NativeSerialization.Deserialize<string>(bytes));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(NativeSerialization.Deserialize<string>(NativeSerialization.Serialize(value))).IsEqualTo(value);
    }

    [Test]
    public async Task AcIs002OnlyOfficialReaderExhaustionIsClassifiedAsMalformedBuffer()
    {
        await Assert.That(NativePayloadSyntax.IsReaderBufferFailure(ReaderFailure())).IsTrue();
        await Assert.That(NativePayloadSyntax.IsReaderBufferFailure(OwnedFailure())).IsFalse();
    }

    private static InvalidOperationException ReaderFailure()
    {
        using var session = NativeSerializerProviders.Get(typeof(string)).Sessions.GetSession();
        var reader = Reader.Create(ReadOnlySpan<byte>.Empty, session);
        try
        {
            _ = reader.ReadFieldHeader();
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
        throw new InvalidOperationException("The genuine reader did not reject an empty buffer.");
    }

    private static InvalidOperationException OwnedFailure()
    {
        try
        {
            throw new InvalidOperationException("Insufficient data present in buffer.");
        }
        catch (InvalidOperationException exception)
        {
            return exception;
        }
    }
}
