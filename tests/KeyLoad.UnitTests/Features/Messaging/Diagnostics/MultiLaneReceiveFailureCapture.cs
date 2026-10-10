using KeyLoad.Orleans;
using Microsoft.Extensions.Logging;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class MultiLaneReceiveFailureCapture(int maximumRecords) : ILoggerProvider
{
    private const int NativeFailureEventId = 3;
    private const string StageKey = "Stage";
    private const string CategoryKey = "Category";
    private const string ErrorCodeKey = "ErrorCode";
    private readonly Lock gate = new();
    private readonly List<Entry> records = [];
    private bool exhausted;
    private bool disposed;
    private string Summary { get; set; } = string.Empty;
    internal string LatestSummary { get { lock (gate) { return Summary; } } }
    internal int Count { get { lock (gate) { return records.Count; } } }
    public ILogger CreateLogger(string categoryName)
        => new CaptureLogger(this, categoryName == typeof(ConnectionGrain).FullName);
    public void Dispose() { lock (gate) { disposed = true; } }

    private void Capture<TState>(EventId id, TState state)
    {
        if (id.Id != NativeFailureEventId
            || state is not IEnumerable<KeyValuePair<string, object?>> fields)
        { return; }
        GrainFailureStage? stage = null;
        GrainFailureCategory? category = null;
        ErrorCode? errorCode = null;
        foreach (var field in fields)
        {
            if (field.Key == StageKey && field.Value is GrainFailureStage actualStage && Enum.IsDefined(actualStage))
            { stage = actualStage; }
            if (field.Key == CategoryKey && field.Value is GrainFailureCategory actualCategory && Enum.IsDefined(actualCategory))
            { category = actualCategory; }
            if (field.Key == ErrorCodeKey && field.Value is ErrorCode actualCode && Enum.IsDefined(actualCode))
            { errorCode = actualCode; }
        }
        if (stage is not { } validStage || category is not { } validCategory || errorCode is not { } validCode)
        { return; }
        lock (gate)
        {
            if (disposed)
            { return; }
            if (records.Count >= maximumRecords)
            { exhausted = true; return; }
            records.Add(new(validStage, validCategory, validCode));
        }
    }

    internal void SetSummary(int originalCount, GrainOperationReply reply, int maximumDetailCharacters)
    {
        lock (gate)
        {
            var detail = reply.SafeDetail;
            var safeDetail = detail is null || detail.Length <= maximumDetailCharacters
                ? detail : InvalidReplyDetail;
            Summary = FormattableString.Invariant($"MultiLane native reply Error={reply.Error}; SafeDetail={safeDetail}; Event3Exhausted={exhausted}; ")
                + string.Join(Separator, records.Skip(originalCount).Select(static item =>
                    FormattableString.Invariant($"stage={item.Stage}/category={item.Category}/code={item.Code}")));
        }
    }
    private const string InvalidReplyDetail = "InvalidReturnedDetailShape";
    private const string Separator = "; ";
    private readonly record struct Entry(GrainFailureStage Stage, GrainFailureCategory Category, ErrorCode Code);
    private sealed class CaptureLogger(MultiLaneReceiveFailureCapture owner, bool enabled) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => enabled;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        { if (enabled) { owner.Capture(eventId, state); } }
    }
}
