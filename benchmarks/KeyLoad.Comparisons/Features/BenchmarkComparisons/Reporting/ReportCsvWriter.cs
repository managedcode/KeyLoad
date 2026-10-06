using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Writes every raw benchmark attempt without retaining a second sample corpus.</summary>
internal static class ReportCsvWriter
{
    private const string Header = "target,scenario,repetition,operation,worker,started_ms,completed_ms,latency_ms,success,error,payload_bytes,completed_message_id,enqueue_ms,receive_ms,ack_ms\n";
    private const string Separator = ",";
    private const string Quote = "\"";
    private const string DoubledQuote = "\"\"";
    private const char QuoteCharacter = '"';
    private const string Empty = "";
    private static readonly UTF8Encoding Utf8WithoutPreamble = new(false);

    internal static async Task WriteAsync(ComparisonReport report, string path, Func<double?, string> number,
        IOptions<NativeComparisonExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var execution = NativeComparisonExecutionOptions.Require(executionOptions).Value;
        cancellationToken.ThrowIfCancellationRequested();
        await using var file = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = execution.ReportFileBufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });
        await using var writer = new StreamWriter(file, Utf8WithoutPreamble, execution.ReportWriterBufferCharacters, leaveOpen: true);
        await writer.WriteAsync(Header.AsMemory(), cancellationToken).ConfigureAwait(false);
        foreach (var item in report.Cases)
        {
            foreach (var sample in item.Samples)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteSampleAsync(writer, item, sample, number, cancellationToken).ConfigureAwait(false);
            }
        }
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteSampleAsync(StreamWriter writer, ComparisonCase item, OperationSample sample,
        Func<double?, string> number, CancellationToken cancellationToken)
    {
        await WriteQuotedAsync(writer, item.Target, cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, item.Scenario.ToString(), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, item.Repetition.ToString(CultureInfo.CurrentCulture), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, sample.Operation.ToString(CultureInfo.CurrentCulture), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, sample.Worker.ToString(CultureInfo.CurrentCulture), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, number(sample.StartedMs), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, number(sample.CompletedMs), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, number(sample.LatencyMs), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, sample.Success.ToString(), cancellationToken).ConfigureAwait(false);
        await QuotedColumnAsync(writer, sample.Error ?? Empty, cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, sample.PayloadBytes.ToString(CultureInfo.CurrentCulture), cancellationToken).ConfigureAwait(false);
        await QuotedColumnAsync(writer, sample.CompletedMessageId ?? Empty, cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, number(sample.Queue?.EnqueueMs), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, number(sample.Queue?.ReceiveMs), cancellationToken).ConfigureAwait(false);
        await ColumnAsync(writer, number(sample.Queue?.AckMs), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(Environment.NewLine.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ColumnAsync(StreamWriter writer, string value, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(Separator.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(value.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask QuotedColumnAsync(StreamWriter writer, string value, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(Separator.AsMemory(), cancellationToken).ConfigureAwait(false);
        await WriteQuotedAsync(writer, value, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask WriteQuotedAsync(StreamWriter writer, string value, CancellationToken cancellationToken)
    {
        const int FirstElementIndex = 0;
        const int NoObservedItems = 0;
        const int AdjacentElementOffset = 1;

        await writer.WriteAsync(Quote.AsMemory(), cancellationToken).ConfigureAwait(false);
        var position = FirstElementIndex;
        while (position < value.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = value.IndexOf(QuoteCharacter, position);
            if (next < NoObservedItems)
            {
                break;
            }
            await writer.WriteAsync(value.AsMemory(position, next - position), cancellationToken).ConfigureAwait(false);
            await writer.WriteAsync(DoubledQuote.AsMemory(), cancellationToken).ConfigureAwait(false);
            position = next + AdjacentElementOffset;
        }
        await writer.WriteAsync(value.AsMemory(position), cancellationToken).ConfigureAwait(false);
        await writer.WriteAsync(Quote.AsMemory(), cancellationToken).ConfigureAwait(false);
    }
}
