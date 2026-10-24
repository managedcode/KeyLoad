namespace KeyLoad.UnitTests.InternalSerialization;

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
}
