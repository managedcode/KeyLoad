using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

/// <summary>AC-CQ-034/035: actual serializer buffer sinks retain the independent byte contracts.</summary>
internal sealed class SerializationBufferPolicyTests
{
    private const string InputJson = "{\"z\":1.2300,\"m\":{\"b\":true,\"a\":\"s\"},\"a\":[null,false]}";
    private const string CanonicalJson = "{\"a\":[null,false],\"m\":{\"a\":\"s\",\"b\":true},\"z\":1.23}";

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    [Arguments(65_536)]
    public async Task ConfiguredCanonicalThresholdReachesNativeFlushAndPreservesFrozenBytesAsync(int flushPendingBytes)
    {
        var options = Bind(nameof(SerializationExecutionOptions.CanonicalJsonFlushPendingBytes), flushPendingBytes);
        using var document = JsonDocument.Parse(InputJson);
        using var bytes = new MemoryStream();
        using var writer = new Utf8JsonWriter(bytes);
        CanonicalJsonWriter.Write(writer, document.RootElement, options);
        await Assert.That(writer.BytesPending).IsLessThan(flushPendingBytes);
        if (flushPendingBytes == 65_536)
        {
            await Assert.That(writer.BytesCommitted).IsEqualTo(0L);
            await Assert.That(bytes.Length).IsEqualTo(0L);
        }
        else
        {
            await Assert.That(writer.BytesCommitted).IsGreaterThan(0L);
            await Assert.That(bytes.Length).IsEqualTo(writer.BytesCommitted);
        }
        await writer.FlushAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(bytes.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(CanonicalJson))).IsTrue();
    }

    [Test]
    [Arguments(nameof(SerializationExecutionOptions.CanonicalJsonFlushPendingBytes), 0)]
    [Arguments(nameof(SerializationExecutionOptions.CanonicalJsonFlushPendingBytes), -1)]
    [Arguments(nameof(SerializationExecutionOptions.CanonicalJsonFlushPendingBytes), 65_537)]
    [Arguments(nameof(SerializationExecutionOptions.DigestUtf8StackBytes), 0)]
    [Arguments(nameof(SerializationExecutionOptions.DigestUtf8StackBytes), -1)]
    [Arguments(nameof(SerializationExecutionOptions.DigestUtf8StackBytes), 257)]
    public async Task InvalidNativeBufferBindingRejectsBeforeOwnerCreationAsync(string property, int value)
    {
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => Bind(property, value));
        await Assert.That(failure.OptionsName).IsEqualTo(Options.DefaultName);
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(SerializationExecutionOptions));
        await Assert.That(failure.Failures.ToArray()).IsEquivalentTo([SerializationExecutionOptions.ValidationMessage]);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    [Arguments(65_537)]
    public async Task InvalidStandaloneCanonicalThresholdRejectsBeforeMutatingNativeWriterAsync(int flushPendingBytes)
    {
        var options = Options.Create(new SerializationExecutionOptions { CanonicalJsonFlushPendingBytes = flushPendingBytes });
        using var document = JsonDocument.Parse(InputJson);
        using var bytes = new MemoryStream();
        using var writer = new Utf8JsonWriter(bytes);
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() =>
            CanonicalJsonWriter.Write(writer, document.RootElement, options));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(SerializationExecutionOptions));
        await Assert.That(writer.BytesPending).IsEqualTo(0);
        await Assert.That(writer.BytesCommitted).IsEqualTo(0L);
        await Assert.That(bytes.Length).IsEqualTo(0L);
    }

    private static IOptions<SerializationExecutionOptions> Bind(string property, int value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            [new KeyValuePair<string, string?>(property, value.ToString(CultureInfo.InvariantCulture))]).Build();
        using var lifetime = configuration as IDisposable;
        return SerializationExecutionRegistration.Bind(configuration);
    }
}
