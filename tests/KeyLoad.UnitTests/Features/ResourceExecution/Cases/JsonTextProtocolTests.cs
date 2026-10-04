using System.Text;
using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class JsonTextProtocolTests
{
    private const string LiteralUnicodeJson = "\"雪🌍\"";
    private const string EscapedUnicodeJson = "\"\\u96EA\\uD83C\\uDF0D\"";
    private const string RawUnpairedSurrogateJson = "\"\uD800\"";
    private const string EscapedUnpairedSurrogateJson = "\"\\uD800\"";
    private const string ReplacementCharacter = "\uFFFD";
    private const string DecodedUnicode = "雪🌍";
    private const string MalformedJson = "{";
    private const string NullIndexesJson = "{\"name\":\"docs\",\"kind\":\"Collection\",\"transactionDomainId\":\"domain\",\"indexes\":null}";
    private const string MissingIndexesJson = "{\"name\":\"docs\",\"kind\":\"Collection\",\"transactionDomainId\":\"domain\"}";
    private const string ValidBytesJson = "{\"key\":\"AQID\",\"value\":\"BAUG\"}";
    private const string InvalidBytesJson = "{\"key\":\"not-base64!\",\"value\":\"BAUG\"}";
    private const string NullBytesJson = "{\"key\":null,\"value\":\"BAUG\"}";
    private const string StoredErrorDetail = "stored failure";
    private const int OversizedResultCharacters = 300_000;
    private const char OversizedResultCharacter = 'x';

    [Test]
    public async Task AcMp006TextAndBytePathsPreserveLiteralEscapeAndSurrogateReplacement()
    {
        foreach (var json in new[] { LiteralUnicodeJson, EscapedUnicodeJson, RawUnpairedSurrogateJson })
        {
            var expected = JsonDefaults.Deserialize<string>(Encoding.UTF8.GetBytes(json));
            var actual = JsonDefaults.Deserialize<string>(json);
            await Assert.That(actual).IsEqualTo(expected);
            var result = new OperationResult(null) { NativeValue = actual };
            var restored = NativeSerialization.Deserialize<OperationResult>(NativeSerialization.Serialize(result));
            await Assert.That(restored.Get<string>()).IsEqualTo(expected);
        }

        await Assert.That(JsonDefaults.Deserialize<string>(RawUnpairedSurrogateJson)).IsEqualTo(ReplacementCharacter);
        await Assert.That(JsonDefaults.Deserialize<string>(LiteralUnicodeJson)).IsEqualTo(DecodedUnicode);
        Assert.ThrowsExactly<JsonException>(() =>
            JsonDefaults.Deserialize<string>(Encoding.UTF8.GetBytes(EscapedUnpairedSurrogateJson)));
        Assert.ThrowsExactly<JsonException>(() =>
            JsonDefaults.Deserialize<string>(EscapedUnpairedSurrogateJson));
    }

    [Test]
    public async Task AcMp006StrictTextNullDefaultCollectionAndBase64MatchByteProtocol()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => JsonDefaults.Deserialize<string>((string)null!));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<ResourceDefinition>(NullIndexesJson));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(InvalidBytesJson));
        Assert.ThrowsExactly<JsonException>(() => JsonDefaults.Deserialize<KeyValueRecord>(NullBytesJson));
        var resource = JsonDefaults.Deserialize<ResourceDefinition>(MissingIndexesJson);
        var bytes = JsonDefaults.Deserialize<KeyValueRecord>(ValidBytesJson);

        await Assert.That(resource.Indexes.Length).IsEqualTo(0);
        await Assert.That(bytes.Key.Span.SequenceEqual<byte>([1, 2, 3])).IsTrue();
        await Assert.That(bytes.Value.Span.SequenceEqual<byte>([4, 5, 6])).IsTrue();
        await Assert.That(JsonDefaults.Serialize(bytes).SequenceEqual(Encoding.UTF8.GetBytes(ValidBytesJson))).IsTrue();
    }

    [Test]
    public async Task AcMp006StoredErrorPrecedesMalformedJsonAndAbsentResultRemainsCorruption()
    {
        var stored = new OperationResult(MalformedJson, ErrorCode.PermissionDenied, StoredErrorDetail);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => stored.Get<ResourceDefinition>());
        var malformed = Assert.ThrowsExactly<KeyLoadException>(() => new OperationResult(MalformedJson).Get<ResourceDefinition>());
        var absent = Assert.ThrowsExactly<KeyLoadException>(() => new OperationResult(null).Get<string>());

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(failure.Message).IsEqualTo(StoredErrorDetail);
        await Assert.That(absent.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(malformed.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcMp006OwnedDtoSurvivesLaterParseAndOversizedResultRemainsExact()
    {
        var first = JsonDefaults.Deserialize<KeyValueRecord>(ValidBytesJson);
        var oversized = new string(OversizedResultCharacter, OversizedResultCharacters);
        var later = JsonDefaults.Deserialize<string>(JsonSerializer.Serialize(oversized, JsonDefaults.Options));
        JsonDefaults.Deserialize<KeyValueRecord>(ValidBytesJson);

        await Assert.That(first.Key.Span.SequenceEqual<byte>([1, 2, 3])).IsTrue();
        await Assert.That(first.Value.Span.SequenceEqual<byte>([4, 5, 6])).IsTrue();
        await Assert.That(later).IsEqualTo(oversized);
    }
}
