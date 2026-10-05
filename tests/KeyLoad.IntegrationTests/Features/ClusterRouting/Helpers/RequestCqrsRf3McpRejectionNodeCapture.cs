using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Retains bounded closed rejection records for one actual RF3 node.</summary>
internal sealed class RequestCqrsRf3McpRejectionNodeCapture(string name)
{
    private const string MessagePrefix = "MCP transport rejected at ";
    private const string Separator = " for ";
    private const string MissingRecordMessage = "The expected closed MCP rejection was not published before the resource stream completed.";
    private const string RecordLimitMessage = "The bounded node diagnostics reached its record limit before the expected rejection.";
    internal const int MaximumLineCharacters = 4_096;
    internal const int MaximumRecords = 32;

    private readonly object gate = new();
    private readonly List<RequestCqrsRf3McpRejectionRecord> records = [];
    private TaskCompletionSource<bool> changed = NewChangeSignal();
    private bool completed;

    internal string Name { get; } = name;

    internal void TryCapture(string line, Guid waveId)
    {
        if (!TryParse(line, Name, waveId, out var record))
        { return; }
        lock (gate)
        {
            if (records.Count >= MaximumRecords)
            { return; }
            records.Add(record);
            var signal = changed;
            changed = NewChangeSignal();
            signal.TrySetResult(true);
        }
    }

    internal async Task WaitForRecordAsync(McpTransportStage stage,
        McpTransportMethodCategory methodCategory, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task signal;
            lock (gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (records.Any(record => record.Stage == stage && record.MethodCategory == methodCategory))
                { return; }
                if (completed)
                { throw new InvalidOperationException(MissingRecordMessage); }
                if (records.Count >= MaximumRecords)
                { throw new InvalidOperationException(RecordLimitMessage); }
                signal = changed.Task;
            }
            await signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    internal void Complete()
    {
        lock (gate)
        {
            completed = true;
            var signal = changed;
            changed = NewChangeSignal();
            signal.TrySetResult(true);
        }
    }

    internal RequestCqrsRf3McpRejectionRecord[] Snapshot()
    {
        lock (gate)
        { return [.. records]; }
    }

    private static TaskCompletionSource<bool> NewChangeSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool TryParse(string line, string node, Guid waveId,
        out RequestCqrsRf3McpRejectionRecord record)
    {
        record = null!;
        if (line.Length > MaximumLineCharacters)
        { return false; }
        var marker = line.IndexOf(MessagePrefix, StringComparison.Ordinal);
        if (marker < 0 || line.IndexOf(MessagePrefix, marker + MessagePrefix.Length,
            StringComparison.Ordinal) >= 0)
        { return false; }

        var message = line.AsSpan(marker + MessagePrefix.Length).TrimEnd("\r\n");
        var separator = message.IndexOf(Separator, StringComparison.Ordinal);
        if (separator <= 0 || message[^1] != '.')
        { return false; }
        var stageText = message[..separator];
        var methodText = message[(separator + Separator.Length)..^1];
        if (!Enum.TryParse<McpTransportStage>(stageText, false, out var stage)
            || !Enum.IsDefined(stage)
            || !stageText.SequenceEqual(stage.ToString())
            || !Enum.TryParse<McpTransportMethodCategory>(methodText, false, out var method)
            || !Enum.IsDefined(method) || !methodText.SequenceEqual(method.ToString()))
        { return false; }

        record = new(waveId, node, stage, method);
        return true;
    }
}
