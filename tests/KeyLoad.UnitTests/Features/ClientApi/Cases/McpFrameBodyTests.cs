using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/004/005: bounded frame ownership preserves the exact wire bytes through native SDK replay.</summary>
internal sealed class McpFrameBodyTests
{
    private const int MaximumBodyBytes = 64;
    private const string SecretMarker = "private-frame-marker";

    /// <summary>A declared-length read retains exact UTF-8 bytes, shape, and a fresh rewindable reader.</summary>
    [Test]
    public async Task DeclaredLengthBodyRetainsAndReplaysExactUtf8WireBytes()
    {
        var wire = Encoding.UTF8.GetBytes("{\"value\":\"é\"}");
        using var source = new TemporaryFrameFile(wire);
        using var input = source.OpenRead();
        using var body = await McpFrameBody.ReadAsync(input, wire.Length, MaximumBodyBytes, UnitMcpOptions.Execution());

        await Assert.That(body.WireBytes).IsEqualTo(wire.Length);
        await Assert.That(body.Shape.TokenCount).IsEqualTo(4);
        await Assert.That(body.Shape.PropertyCount).IsEqualTo(1);
        await Assert.That(body.Bytes.Span.SequenceEqual(wire)).IsTrue();

        var replayed = new byte[wire.Length];
        var firstReader = body.OpenReader();
        await firstReader.ReadExactlyAsync(replayed.AsMemory());
        await Assert.That(replayed.AsSpan().SequenceEqual(wire)).IsTrue();
        using var rewoundReader = body.OpenReader();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() => firstReader.ReadAsync(new byte[1].AsMemory()).AsTask());
        var firstByte = new byte[1];
        await rewoundReader.ReadExactlyAsync(firstByte.AsMemory());
        await Assert.That(firstByte[0]).IsEqualTo(wire[0]);
        var borrowed = body.Bytes;
        body.Dispose();
        await Assert.That(borrowed.Span.SequenceEqual(new byte[borrowed.Length])).IsTrue();
    }

    /// <summary>An unknown declared length accepts a genuine file stream and produces the same bounded shape.</summary>
    [Test]
    public async Task UnknownDeclaredLengthReadsThroughEndOfFile()
    {
        var wire = Encoding.UTF8.GetBytes("{\"items\":[1,2]}");
        using var source = new TemporaryFrameFile(wire);
        using var input = source.OpenRead();
        using var body = await McpFrameBody.ReadAsync(input, null, MaximumBodyBytes, UnitMcpOptions.Execution());

        await Assert.That(body.WireBytes).IsEqualTo(wire.Length);
        await Assert.That(body.Shape.PropertyCount).IsEqualTo(1);
        await Assert.That(body.Bytes.Span.SequenceEqual(wire)).IsTrue();
    }

    /// <summary>The actual wire body is accepted when its byte count exactly matches the inclusive configured limit.</summary>
    [Test]
    public async Task ActualBodyAtMaximumByteBoundaryIsAccepted()
    {
        var json = $"{{\"x\":\"{new string('a', MaximumBodyBytes - 8)}\"}}";
        var wire = Encoding.UTF8.GetBytes(json);
        await Assert.That(wire.Length).IsEqualTo(MaximumBodyBytes);
        using var source = new TemporaryFrameFile(wire);
        using var input = source.OpenRead();
        using var body = await McpFrameBody.ReadAsync(input, MaximumBodyBytes, MaximumBodyBytes, UnitMcpOptions.Execution());

        await Assert.That(body.WireBytes).IsEqualTo(MaximumBodyBytes);
        await Assert.That(body.Bytes.Span.SequenceEqual(wire)).IsTrue();
    }

    /// <summary>Nonpositive limits are rejected before reading from the actual file stream.</summary>
    [Test]
    public async Task MaximumBodyLimitMustBePositive()
    {
        using var source = new TemporaryFrameFile(Encoding.UTF8.GetBytes("{}"));
        using var input = source.OpenRead();

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => McpFrameBody.ReadAsync(input, null, 0, UnitMcpOptions.Execution()));
        await Assert.That(input.Position).IsEqualTo(0);
    }

    /// <summary>Negative or over-limit declared sizes fail before consuming any stream bytes.</summary>
    /// <param name="declaredLength">The invalid wire length supplied by the framing layer.</param>
    [Test]
    [Arguments(-1L)]
    [Arguments(65L)]
    public async Task InvalidDeclaredLengthFailsBeforeRead(long declaredLength)
    {
        using var source = new TemporaryFrameFile(Encoding.UTF8.GetBytes("{}"));
        using var input = source.OpenRead();

        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            McpFrameBody.ReadAsync(input, declaredLength, MaximumBodyBytes, UnitMcpOptions.Execution())) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(input.Position).IsEqualTo(0);
    }

    /// <summary>A declared size that differs from the complete stream length is a fixed safe validation failure.</summary>
    /// <param name="declaredLength">A declared size shorter or longer than the actual file contents.</param>
    [Test]
    [Arguments(1L)]
    [Arguments(3L)]
    public async Task DeclaredLengthMustMatchActualStream(long declaredLength)
    {
        using var source = new TemporaryFrameFile(Encoding.UTF8.GetBytes("{}"));
        using var input = source.OpenRead();

        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            McpFrameBody.ReadAsync(input, declaredLength, MaximumBodyBytes, UnitMcpOptions.Execution())) ?? throw new InvalidOperationException();
        using var malformedSource = new TemporaryFrameFile(Encoding.UTF8.GetBytes("{\"invalid\":]}"));
        using var malformedInput = malformedSource.OpenRead();
        var malformed = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            McpFrameBody.ReadAsync(malformedInput, null, MaximumBodyBytes, UnitMcpOptions.Execution())) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(malformed.Message);
    }

    /// <summary>An unknown-length stream over the byte ceiling is rejected after at most one detection byte.</summary>
    [Test]
    public async Task UnknownLengthOverrunStopsAtMaximumPlusOne()
    {
        var oversized = new byte[MaximumBodyBytes + 20];
        using var source = new TemporaryFrameFile(oversized);
        using var input = source.OpenRead();

        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            McpFrameBody.ReadAsync(input, null, MaximumBodyBytes, UnitMcpOptions.Execution())) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(input.Position).IsLessThanOrEqualTo(MaximumBodyBytes + 1L);
    }

    /// <summary>Malformed JSON uses the fixed framing detail and never reflects the private input marker.</summary>
    [Test]
    public async Task MalformedBodyUsesFixedSafeValidationDetail()
    {
        using var source = new TemporaryFrameFile(Encoding.UTF8.GetBytes($"{{\"secret\":\"{SecretMarker}\",\"bad\":]}}"));
        using var input = source.OpenRead();

        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(() =>
            McpFrameBody.ReadAsync(input, null, MaximumBodyBytes, UnitMcpOptions.Execution())) ?? throw new InvalidOperationException();
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).DoesNotContain(SecretMarker);
    }

    /// <summary>An already-cancelled token preserves cancellation and leaves the file stream unread.</summary>
    [Test]
    public async Task CancellationRemainsCancellationBeforeBodyRead()
    {
        using var source = new TemporaryFrameFile(Encoding.UTF8.GetBytes("{}"));
        using var input = source.OpenRead();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
            McpFrameBody.ReadAsync(input, null, MaximumBodyBytes, UnitMcpOptions.Execution(), cancelled.Token));
        await Assert.That(input.Position).IsEqualTo(0);
    }

    /// <summary>A disposed actual file stream faults the read instead of producing a fabricated body.</summary>
    [Test]
    public async Task DisposedFileStreamFaultsRead()
    {
        using var source = new TemporaryFrameFile(Encoding.UTF8.GetBytes("{}"));
        await using var input = source.OpenRead();
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(() =>
            DisposeThenReadAsync(input));
    }

    /// <summary>Disposal is idempotent, zeroes borrowed bytes, and rejects all later access.</summary>
    [Test]
    public async Task DisposalClearsBorrowedBytesAndGuardsAccess()
    {
        var wire = Encoding.UTF8.GetBytes("{\"private\":\"payload\"}");
        using var source = new TemporaryFrameFile(wire);
        using var input = source.OpenRead();
        var body = await McpFrameBody.ReadAsync(input, wire.Length, MaximumBodyBytes, UnitMcpOptions.Execution());
        var borrowed = body.Bytes;
        await Assert.That(borrowed.Span.SequenceEqual(wire)).IsTrue();

        body.Dispose();
        body.Dispose();
        await Assert.That(borrowed.Span.SequenceEqual(new byte[borrowed.Length])).IsTrue();
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = body.WireBytes);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = body.Shape);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = body.Bytes);
        Assert.ThrowsExactly<ObjectDisposedException>(() => body.OpenReader());
    }

    private static async Task<McpFrameBody> DisposeThenReadAsync(FileStream input)
    {
        await input.DisposeAsync();
        return await McpFrameBody.ReadAsync(input, null, MaximumBodyBytes, UnitMcpOptions.Execution());
    }
}

/// <summary>Owns one real temporary wire file used by MCP frame contract tests.</summary>
internal sealed class TemporaryFrameFile : IDisposable
{
    private const string FilePrefix = "keyload-mcp-frame-";
    private const string FileExtension = ".json";

    /// <summary>Creates an isolated temporary file containing the supplied real wire bytes.</summary>
    /// <param name="content">The exact bytes to write before opening a FileStream.</param>
    public TemporaryFrameFile(byte[] content)
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{FilePrefix}{Guid.NewGuid():N}{FileExtension}");
        File.WriteAllBytes(Path, content);
    }

    /// <summary>Gets the unique temporary file path.</summary>
    public string Path { get; }

    /// <summary>Opens the temporary bytes through a real asynchronous FileStream.</summary>
    /// <returns>The open file stream positioned at its beginning.</returns>
    public FileStream OpenRead()
        => new(Path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);

    /// <summary>Deletes the temporary wire file after its streams have closed.</summary>
    public void Dispose()
    {
        if (File.Exists(Path))
        {
            File.Delete(Path);
        }
    }
}
