using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

/// <summary>AC-IS-003/007: execution serializers preserve canonical bytes across buffer boundaries.</summary>
internal sealed class SerializationExecutionPolicyTests
{
    [Test]
    [Arguments(4095)]
    [Arguments(4097)]
    [Arguments(4096)]
    public async Task PoolAndFingerprintPreserveCanonicalBytesAndOriginalShapeAsync(int boundaryCharacters)
    {
        const string TextKey = "text";
        const string EmptyKey = "empty";
        const string FollowingKey = "other";

        var payload = new string('x', boundaryCharacters - 1) + "😀界λ\"\\\n<&" + new string('y', 8192);
        var expected = new Dictionary<string, string> { [TextKey] = payload, [EmptyKey] = string.Empty };
        var json = JsonDefaults.Serialize(expected);
        var decoded = PooledJsonText.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(json));
        var following = PooledJsonText.Deserialize<Dictionary<string, string>>("{\"other\":\"following\"}");
        await Assert.That(following[FollowingKey]).IsEqualTo("following");
        await Assert.That(JsonDefaults.Serialize(decoded).SequenceEqual(json)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(decoded[TextKey]).SequenceEqual(
            OrleansSerialization.Serialize(payload))).IsTrue();
        var id = new Guid(FingerprintFixtures.Id);
        const string Principal = "root界\\\"😀";
        var authored = payload + "\uD800";
        var fingerprint = JsonData.Fingerprint(new
        {
            Id = id,
            Kind = OperationKind.Batch,
            PrincipalId = Principal,
            PayloadJson = authored
        });
        await Assert.That(DatabaseCommandFingerprint.Compute(id, OperationKind.Batch, Principal, authored))
            .IsEqualTo(fingerprint);
    }

    [Test]
    [Arguments(4095)]
    [Arguments(4097)]
    [Arguments(4096)]
    public async Task DomSegmentsPreserveWholeStringEscapingAndSurrogateProgressAsync(int boundaryCharacters)
    {
        var owner = new DomStringSizes();
        var boundary = new string('x', boundaryCharacters - 1);
        foreach (var text in new[]
        {
            string.Empty, DomFixtures.EscapedText, boundary + "🙂" + DomFixtures.EscapedText
        })
        {
            var expected = (long)JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;
            await Assert.That(owner.Escaped(text)).IsEqualTo(expected);
            await Assert.That(owner.Escaped(text)).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments(4095)]
    [Arguments(4097)]
    [Arguments(4096)]
    public async Task DomSegmentsRejectMalformedUtf16WithFailureAsync(int boundaryCharacters)
    {
        var owner = new DomStringSizes();
        var boundary = new string('x', boundaryCharacters - 1);
        foreach (var text in new[] { "\uD800", "\uDC00", boundary + "\uD800\uD800\uDC00\uDC00" + boundary })
        {
            var execution = Assert.ThrowsExactly<ArgumentException>(() => JsonEncodedText.Encode(text));
            var actual = Assert.ThrowsExactly<ArgumentException>(() => owner.Escaped(text));
            var repeated = Assert.ThrowsExactly<ArgumentException>(() => owner.Escaped(text));
            await Assert.That(actual.Message).IsEqualTo(execution.Message);
            await Assert.That(actual.ParamName).IsEqualTo(execution.ParamName);
            await Assert.That(actual.InnerException is EncoderFallbackException).IsTrue();
            await Assert.That(execution.InnerException is EncoderFallbackException).IsTrue();
            await Assert.That(repeated.Message).IsEqualTo(execution.Message);
        }
        var following = boundary + "🙂" + DomFixtures.EscapedText;
        var expected = (long)JsonEncodedText.Encode(following).EncodedUtf8Bytes.Length;
        await Assert.That(owner.Escaped(following)).IsEqualTo(expected);
        await Assert.That(owner.Escaped(following)).IsEqualTo(expected);
    }
}
