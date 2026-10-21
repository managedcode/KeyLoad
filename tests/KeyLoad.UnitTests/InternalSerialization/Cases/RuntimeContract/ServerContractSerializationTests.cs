using System.Security.Cryptography;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ServerContractSerializationTests
{
    [Test]
    public async Task ServerEnvelopeRetainsExactGeneratedPayloadAndChecksum()
    {
        var payload = new byte[] { 0, 1, 127, 128, 255 };
        var checksum = SHA256.HashData(payload);
        var original = new TextEnvelope(TextProtocol.FormatVersion, payload, checksum);
        var decoded = OrleansSerialization.Deserialize<TextEnvelope>(OrleansSerialization.Serialize(original));
        await Assert.That(decoded.FormatVersion).IsEqualTo(original.FormatVersion);
        await Assert.That(decoded.Payload.AsSpan().SequenceEqual(payload)).IsTrue();
        await Assert.That(decoded.Sha256.AsSpan().SequenceEqual(checksum)).IsTrue();
    }

    [Test]
    public async Task FreshServerReceiptSurvivesCheckedEnvelopeRoundTrip()
    {
        var original = new TextOwnerReceipt(TextProtocol.FormatVersion, AppContext.BaseDirectory,
            string.Empty, Guid.NewGuid(), null, []);
        var decoded = TextEnvelopeCodec.Decode<TextOwnerReceipt>(TextEnvelopeCodec.Encode(original));
        await Assert.That(decoded.FormatVersion).IsEqualTo(original.FormatVersion);
        await Assert.That(decoded.RootDirectory).IsEqualTo(original.RootDirectory);
        await Assert.That(decoded.SourceNodeId).IsEqualTo(original.SourceNodeId);
        await Assert.That(decoded.GenerationLeaf).IsEmpty();
        await Assert.That(decoded.Scope).IsNull();
        await Assert.That(decoded.OwnedPaths).IsEmpty();
    }
}
