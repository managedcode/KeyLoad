using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

/// <summary>AC-IS-007: actual serializer buffer sinks retain the independent byte contracts.</summary>
internal sealed class SerializationBufferPolicyTests
{
    private const string InputJson = "{\"z\":1.2300,\"m\":{\"b\":true,\"a\":\"s\"},\"a\":[null,false]}";
    private const string CanonicalJson = "{\"a\":[null,false],\"m\":{\"a\":\"s\",\"b\":true},\"z\":1.23}";

    [Test]
    public async Task CanonicalWriterPreservesFrozenBytesAsync()
    {
        using var document = JsonDocument.Parse(InputJson);
        using var bytes = new MemoryStream();
        using var writer = new Utf8JsonWriter(bytes);
        CanonicalJsonWriter.Write(writer, document.RootElement);
        await Assert.That(writer.BytesPending).IsLessThan(65_536);
        await Assert.That(writer.BytesCommitted).IsEqualTo(0L);
        await Assert.That(bytes.Length).IsEqualTo(0L);
        await writer.FlushAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(bytes.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(CanonicalJson))).IsTrue();
    }

    [Test]
    public async Task LargeCanonicalValueFlushesAndPreservesContentAsync()
    {
        var text = new string('x', 65_536) + "😀界";
        var json = JsonSerializer.Serialize(text);
        using var document = JsonDocument.Parse(json);
        using var bytes = new MemoryStream();
        using var writer = new Utf8JsonWriter(bytes);
        CanonicalJsonWriter.Write(writer, document.RootElement);
        await Assert.That(writer.BytesCommitted).IsGreaterThan(0L);
        await Assert.That(bytes.Length).IsEqualTo(writer.BytesCommitted);
        await writer.FlushAsync(TestContext.Current!.Execution.CancellationToken);
        await Assert.That(bytes.ToArray().SequenceEqual(Encoding.UTF8.GetBytes(json))).IsTrue();
    }
}
