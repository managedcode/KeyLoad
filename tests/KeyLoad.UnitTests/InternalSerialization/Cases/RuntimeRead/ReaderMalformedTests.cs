using System.Text;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReaderMalformedTests
{
    private const string AtomicPartition = "partition";

    [Test]
    public async Task AcIs002EveryTruncatedPrefixIsCodedCorruptionAndSessionsRemainReusable()
    {
        var expected = new DocumentResult(new(new("tenant", "database", "domain", AtomicPartition), "documents", "id"),
            42, "{\"text\":\"Київ🌍\"}", false, []);
        var bytes = OrleansSerialization.Serialize(expected);
        for (var length = 0; length < bytes.Length; length++)
        {
            var decode = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<DocumentResult>(bytes.AsSpan(0, length)));
            var syntax = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Validate(bytes.AsSpan(0, length)));
            await Assert.That(decode.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(syntax.Code).IsEqualTo(ErrorCode.Corruption);
            await Assert.That(decode.Message).IsEqualTo(PayloadVersion.InvalidPayload);
        }
        var actual = OrleansSerialization.Deserialize<DocumentResult>(bytes);
        await Assert.That(actual.Json).IsEqualTo(expected.Json);
        await Assert.That(actual.Reference).IsEqualTo(expected.Reference);
        OrleansSerialization.Validate(bytes);
    }

    [Test]
    public async Task AcIs002InvalidUtf8RemainsRejectedBeforeSuccessfulReaderReuse()
    {
        const string value = "Київ🌍";
        var bytes = OrleansSerialization.Serialize(value);
        var position = bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(value));
        await Assert.That(position >= 0).IsTrue();
        bytes[position] = 0xff;
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => OrleansSerialization.Deserialize<string>(bytes));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(OrleansSerialization.Deserialize<string>(OrleansSerialization.Serialize(value))).IsEqualTo(value);
    }

    [Test]
    public async Task AcIs002OnlyOfficialReaderExhaustionIsClassifiedAsMalformedBuffer()
    {
        await Assert.That(RuntimePayloadSyntax.IsReaderBufferFailure(ReaderFailure())).IsTrue();
        await Assert.That(RuntimePayloadSyntax.IsReaderBufferFailure(OwnedFailure())).IsFalse();
    }

    private static InvalidOperationException ReaderFailure()
    {
        using var session = SerializerProviders.Get(typeof(string)).Sessions.GetSession();
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
