using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed class NativeAnnWaitHttpObservation
{
    private const int MaximumObservations = 8;
    private const int SchemaVersion = 1;
    private const int NoStatus = 0;
    private const string Kind = "NativeAnnFirstBuildHttp";
    private readonly Lock gate = new();
    private readonly List<Observation> observations = [];
    private CancellationToken originalCancellation;
    private bool enabled;
    private bool saturated;

    internal void Start(CancellationToken token)
    {
        lock (gate)
        { originalCancellation = token; enabled = true; }
    }

    internal void Stop()
    {
        lock (gate)
        { enabled = false; }
    }

    internal void Write(List<Exception> failures)
    {
        lock (gate)
        {
            ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                kind = Kind,
                saturated,
                observations
            })), failures);
        }
    }

    internal void Started(CancellationToken token)
        => Record(false, false, NoStatus, FailureCategory.None, token);

    internal void Failed(Exception error, CancellationToken token)
        => Record(true, false, error is HttpRequestException { StatusCode: { } status }
            ? (int)status : NoStatus, Category(error), token);

    internal void Received(int status, CancellationToken token)
        => Record(true, true, status, FailureCategory.None, token);

    private void Record(bool settled, bool response, int status, FailureCategory category,
        CancellationToken actualSendCancellation)
    {
        lock (gate)
        {
            if (!enabled)
            { return; }
            if (observations.Count == MaximumObservations)
            { saturated = true; return; }
            observations.Add(new(true, settled, response, status, category.ToString(),
                actualSendCancellation.IsCancellationRequested, originalCancellation.IsCancellationRequested));
        }
    }

    private static FailureCategory Category(Exception error) => error switch
    {
        OperationCanceledException => FailureCategory.Canceled,
        HttpRequestException => FailureCategory.HttpRequest,
        IOException => FailureCategory.Io,
        _ => FailureCategory.Other
    };

    private enum FailureCategory
    { None, Canceled, HttpRequest, Io, Other }

    private sealed record Observation(bool SendStarted, bool Settled, bool ResponseReceived,
        int HttpStatus, string FailureCategory, bool SendTokenCancelled, bool OriginalTokenCancelled);
}

