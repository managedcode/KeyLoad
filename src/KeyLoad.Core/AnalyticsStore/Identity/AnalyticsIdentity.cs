using System.Text;
using System.Text.Json;
using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core.Features.InternalSerialization;

/// <summary>Canonicalizes only a first-issued typed analytics batch; retained execution operations remain untouched.</summary>
internal static class AnalyticsIdentity
{
    private const int MinimumBytes = 1;
    private const int FirstByte = 0;
    private const string InvalidBudget = "The analytics command identity byte budget is invalid.";
    private const string AnalyticsRequired = "The command has no typed analytics mutation.";

    internal static bool HasAnalyticsMutation(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return !request.Mutations.IsDefaultOrEmpty
            && request.Mutations.Any(mutation => mutation is UpsertAnalyticsEvents or DeleteAnalyticsEvents);
    }

    internal static string Canonicalize(CommandRequest request, ReadExecutionBudget work, int maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (maximumBytes < MinimumBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidBudget);
        }

        if (!HasAnalyticsMutation(request))
        {
            throw new ArgumentException(AnalyticsRequired, nameof(request));
        }

        MeasureTyped(request, work, maximumBytes);
        work.Check();
        using var document = JsonSerializer.SerializeToDocument(request, JsonDefaults.Options);
        work.Check();
        long length;
        using (var measured = new ResultByteCounterStream(maximumBytes, work.Check))
        {
            WriteCanonical(document.RootElement, measured);
            length = measured.Length;
        }
        work.Check();
        using var bytes = new MemoryStream(checked((int)length));
        using var counter = new ResultByteCounterStream(length, work.Check);
        using (var output = new CheckedOutput(bytes, counter))
        {
            WriteCanonical(document.RootElement, output);
        }
        work.Check();
        var identity = Encoding.UTF8.GetString(bytes.GetBuffer().AsSpan(FirstByte, checked((int)bytes.Length)));
        work.Check();
        return identity;
    }

    private static void MeasureTyped(CommandRequest request, ReadExecutionBudget work, int maximumBytes)
    {
        using var counter = new ResultByteCounterStream(maximumBytes, work.Check);
        JsonSerializer.Serialize(counter, request, JsonDefaults.Options);
        work.Check();
    }

    private static void WriteCanonical(JsonElement element, Stream output)
    {
        using var writer = new Utf8JsonWriter(output);
        CanonicalJsonWriter.Write(writer, element, decimalNumbers: false);
        writer.Flush();
    }

    /// <summary>Forwards writes only after the existing byte counter checks the original caller frame.</summary>
    private sealed class CheckedOutput(Stream output, ResultByteCounterStream counter) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => counter.Length;
        public override long Position
        {
            get => counter.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            counter.Flush();
            output.Flush();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            counter.Write(buffer);
            output.Write(buffer);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
