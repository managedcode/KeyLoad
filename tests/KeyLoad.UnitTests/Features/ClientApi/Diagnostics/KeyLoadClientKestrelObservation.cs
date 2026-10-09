using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal enum KestrelObservationStage
{
    HostStarting, HostStarted, SendStarted, ResponseReceived, SendFailed,
    HandlerEntered, HandlerCompleted, HandlerFailed, SdkCompleted, CallerCancelled, FlowFailed
}

/// <summary>Closed bounded actual test lifecycle facts; no request or response contents are inspected.</summary>
internal sealed class KeyLoadClientKestrelObservation
{
    private const int MaximumRecords = 64;
    private const int SchemaVersion = 1;
    private const string Kind = "NativeKestrelFailureObservation";
    private const string None = "None";
    private const string Cancelled = "Cancelled";
    private const string Http = "HttpRequest";
    private const string Io = "Io";
    private const string Other = "Other";
    private readonly Lock gate = new();
    private readonly TimeProvider clock = TimeProvider.System;
    private readonly long started;
    private readonly List<object> records = [];
    private bool saturated;
    private bool nativeServerEventsObserved;

    internal KeyLoadClientKestrelObservation()
    { started = clock.GetTimestamp(); }

    internal void Record(KestrelObservationStage stage, int status = 0, Exception? error = null,
        CancellationToken token = default)
    {
        lock (gate)
        {
            if (records.Count == MaximumRecords)
            { saturated = true; return; }
            ThreadPool.GetAvailableThreads(out var workers, out var io);
            records.Add(new
            {
                stage = stage.ToString(),
                status,
                tokenCancelled = token.IsCancellationRequested,
                exceptionKind = error switch
                {
                    null => None,
                    OperationCanceledException => Cancelled,
                    HttpRequestException => Http,
                    IOException => Io,
                    _ => Other
                },
                elapsedMilliseconds = clock.GetElapsedTime(started).TotalMilliseconds,
                availableWorkers = workers,
                availableIo = io,
                pendingWork = ThreadPool.PendingWorkItemCount,
                threads = ThreadPool.ThreadCount
            });
        }
    }

    internal void RecordNativeServerEvent(string category, int eventId, string level)
    {
        lock (gate)
        {
            nativeServerEventsObserved = true;
            if (records.Count == MaximumRecords)
            { saturated = true; return; }
            records.Add(new
            {
                nativeCategory = category,
                nativeEventId = eventId,
                nativeLogLevel = level,
                elapsedMilliseconds = clock.GetElapsedTime(started).TotalMilliseconds
            });
        }
    }

    internal void WriteAndThrow(Exception original)
    {
        Record(KestrelObservationStage.FlowFailed, error: original);
        var failures = new List<Exception> { original };
        lock (gate)
        {
            ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                kind = Kind,
                saturated,
                nativeServerEventsObserved,
                nativeServerLogLevelOverride = false,
                records
            })), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal HttpClient CreateClient(Uri endpoint)
    {
        var failures = new List<Exception>();
        try
        {
            KeyLoadClientKestrelObservedHandler? pendingHandler = null;
            try
            {
                pendingHandler = new KeyLoadClientKestrelObservedHandler(this);
                AttachTransport(pendingHandler, failures);
                var client = new HttpClient(pendingHandler, disposeHandler: true);
                pendingHandler = null;
                try
                {
                    client.BaseAddress = endpoint;
                    client.Timeout = Timeout.InfiniteTimeSpan;
                    return client;
                }
                catch (Exception original)
                {
                    failures.Add(original);
                    client.Dispose();
                    throw;
                }
            }
            catch (Exception observed)
            {
                RecordFailure(failures, observed);
                throw;
            }
            finally { pendingHandler?.Dispose(); }
        }
        catch (Exception observed)
        {
            RecordFailure(failures, observed);
            ServerFailureObserver.ThrowIfAny(failures);
            throw;
        }
    }
    private static void AttachTransport(KeyLoadClientKestrelObservedHandler handler, List<Exception> failures)
    {
        var transport = new HttpClientHandler();
        try
        { handler.InnerHandler = transport; }
        catch (Exception original)
        {
            failures.Add(original);
            transport.Dispose();
            throw;
        }
    }
    private static void RecordFailure(List<Exception> failures, Exception observed)
    {
        if (!failures.Exists(failure => ReferenceEquals(failure, observed)))
        { failures.Add(observed); }
    }

}
