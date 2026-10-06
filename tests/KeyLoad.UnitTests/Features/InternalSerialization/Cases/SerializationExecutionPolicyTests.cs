using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;
using KeyLoad.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

/// <summary>AC-CQ-034/035: genuine native owners preserve canonical bytes under bound execution policy.</summary>
internal sealed class SerializationExecutionPolicyTests
{
    [Test]
    [Arguments(1, 1, 2)]
    [Arguments(4096, 1, 3)]
    [Arguments(262_144, 2, 4096)]
    public async Task ConfiguredPoolAndFingerprintPreserveCanonicalBytesAndOriginalShapeAsync(
        int retainedArrayBytes, int arraysPerBucket, int chunkCharacters)
    {
        const string TextKey = "text";
        const string EmptyKey = "empty";
        const string FollowingKey = "other";

        var options = Bind(
            (nameof(SerializationExecutionOptions.JsonTextMaximumRetainedArrayBytes), retainedArrayBytes),
            (nameof(SerializationExecutionOptions.JsonTextMaximumArraysPerBucket), arraysPerBucket),
            (nameof(SerializationExecutionOptions.FingerprintChunkCharacters), chunkCharacters));
        var owner = new PooledJsonText.Owner(options);
        var payload = new string('x', chunkCharacters - 1) + "😀界λ\"\\\n<&" + new string('y', 8192);
        var expected = new Dictionary<string, string> { [TextKey] = payload, [EmptyKey] = string.Empty };
        var json = JsonDefaults.Serialize(expected);
        var decoded = owner.Deserialize<Dictionary<string, string>>(Encoding.UTF8.GetString(json));
        var following = owner.Deserialize<Dictionary<string, string>>("{\"other\":\"following\"}");
        await Assert.That(following[FollowingKey]).IsEqualTo("following");
        await Assert.That(JsonDefaults.Serialize(decoded).SequenceEqual(json)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(decoded[TextKey]).SequenceEqual(
            NativeSerialization.Serialize(payload))).IsTrue();
        var operation = new ReplicatedOperation(new Guid(NativeFingerprintFixtures.Id), OperationKind.Batch,
            "root界\\\"😀", DateTimeOffset.UnixEpoch, payload + "\uD800");
        var fingerprint = JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson });
        await Assert.That(NativeOperationFingerprint.Compute(operation, options)).IsEqualTo(fingerprint);
        await Assert.That(NativeOperationFingerprint.Compute(operation.Id, operation.Kind, operation.PrincipalId,
            Encoding.UTF8.GetBytes(operation.PayloadJson), options)).IsEqualTo(fingerprint);
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4096)]
    public async Task ConfiguredDomSegmentsPreserveWholeStringEscapingAndSurrogateProgressAsync(int chunkCharacters)
    {
        var options = Bind((nameof(SerializationExecutionOptions.DomChunkCharacters), chunkCharacters));
        var owner = new NativeDomStringSizes(options);
        var boundary = new string('x', chunkCharacters - 1);
        foreach (var text in new[]
        {
            string.Empty, NativeDomFixtures.EscapedText, boundary + "🙂" + NativeDomFixtures.EscapedText
        })
        {
            var expected = (long)JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;
            await Assert.That(owner.Escaped(text)).IsEqualTo(expected);
            await Assert.That(owner.Escaped(text)).IsEqualTo(expected);
        }
    }

    [Test]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4096)]
    public async Task ConfiguredDomSegmentsRejectMalformedUtf16WithNativeFailureAsync(int chunkCharacters)
    {
        var options = Bind((nameof(SerializationExecutionOptions.DomChunkCharacters), chunkCharacters));
        var owner = new NativeDomStringSizes(options);
        var boundary = new string('x', chunkCharacters - 1);
        foreach (var text in new[] { "\uD800", "\uDC00", boundary + "\uD800\uD800\uDC00\uDC00" + boundary })
        {
            var native = Assert.ThrowsExactly<ArgumentException>(() => JsonEncodedText.Encode(text));
            var actual = Assert.ThrowsExactly<ArgumentException>(() => owner.Escaped(text));
            var repeated = Assert.ThrowsExactly<ArgumentException>(() => owner.Escaped(text));
            await Assert.That(actual.Message).IsEqualTo(native.Message);
            await Assert.That(actual.ParamName).IsEqualTo(native.ParamName);
            await Assert.That(actual.InnerException is EncoderFallbackException).IsTrue();
            await Assert.That(native.InnerException is EncoderFallbackException).IsTrue();
            await Assert.That(repeated.Message).IsEqualTo(native.Message);
        }
        var following = boundary + "🙂" + NativeDomFixtures.EscapedText;
        var expected = (long)JsonEncodedText.Encode(following).EncodedUtf8Bytes.Length;
        await Assert.That(owner.Escaped(following)).IsEqualTo(expected);
        await Assert.That(owner.Escaped(following)).IsEqualTo(expected);
    }

    [Test]
    [Arguments(nameof(SerializationExecutionOptions.JsonTextMaximumRetainedArrayBytes), 0)]
    [Arguments(nameof(SerializationExecutionOptions.JsonTextMaximumRetainedArrayBytes), -1)]
    [Arguments(nameof(SerializationExecutionOptions.JsonTextMaximumRetainedArrayBytes), 262_145)]
    [Arguments(nameof(SerializationExecutionOptions.JsonTextMaximumArraysPerBucket), 0)]
    [Arguments(nameof(SerializationExecutionOptions.JsonTextMaximumArraysPerBucket), -1)]
    [Arguments(nameof(SerializationExecutionOptions.JsonTextMaximumArraysPerBucket), 3)]
    [Arguments(nameof(SerializationExecutionOptions.FingerprintChunkCharacters), 0)]
    [Arguments(nameof(SerializationExecutionOptions.FingerprintChunkCharacters), -1)]
    [Arguments(nameof(SerializationExecutionOptions.FingerprintChunkCharacters), 1)]
    [Arguments(nameof(SerializationExecutionOptions.FingerprintChunkCharacters), 4097)]
    [Arguments(nameof(SerializationExecutionOptions.DomChunkCharacters), 0)]
    [Arguments(nameof(SerializationExecutionOptions.DomChunkCharacters), -1)]
    [Arguments(nameof(SerializationExecutionOptions.DomChunkCharacters), 1)]
    [Arguments(nameof(SerializationExecutionOptions.DomChunkCharacters), 4097)]
    public async Task InvalidNativeBindingRejectsBeforeOwnerCreationAsync(string property, int value)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => Bind((property, value)));
        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(SerializationExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([SerializationExecutionOptions.ValidationMessage]);
    }

    [Test]
    public async Task CanonicalNativeDefaultsAndProcessWrapperRemainDistinctCompositionContractsAsync()
    {
        var configured = Bind();
        await Assert.That(configured.Value.JsonTextMaximumRetainedArrayBytes).IsEqualTo(262_144);
        await Assert.That(configured.Value.JsonTextMaximumArraysPerBucket).IsEqualTo(2);
        await Assert.That(configured.Value.FingerprintChunkCharacters).IsEqualTo(4096);
        await Assert.That(configured.Value.DomChunkCharacters).IsEqualTo(4096);
        await Assert.That(configured.Value.CanonicalJsonFlushPendingBytes).IsEqualTo(65_536);
        await Assert.That(configured.Value.DigestUtf8StackBytes).IsEqualTo(256);
        var process = SerializationExecutionRegistration.Process;
        await Assert.That(SerializationExecutionRegistration.Process).IsSameReferenceAs(process);
        await Assert.That(SerializationExecutionRegistration.Process.Value).IsSameReferenceAs(process.Value);
    }

    private static IOptions<SerializationExecutionOptions> Bind(params (string Property, int Value)[] values)
    {
        var input = values.Select(value => new KeyValuePair<string, string?>(
            value.Property, value.Value.ToString(CultureInfo.InvariantCulture)));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(input).Build();
        using var lifetime = configuration as IDisposable;
        return SerializationExecutionRegistration.Bind(configuration);
    }
}
