using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Observes only the original held SDK send without reading or replacing either body.</summary>
internal sealed class RequestCqrsHeldSdkObservation(HttpMessageHandler inner, TimeProvider clock) : DelegatingHandler(inner)
{
    private const int MaximumObservations = 8;
    private const string CommandHeader = "X-KeyLoad-Command-Id";
    private const string Kind = "NativeHeldSdkRevocationHttp";
    private readonly Lock gate = new();
    private readonly List<object> observations = [];
    private Guid selected;
    private long started;
    private bool saturated;

    internal void Arm(Guid commandId)
    {
        lock (gate)
        {
            selected = commandId;
            started = clock.GetTimestamp();
            Record("Armed");
        }
    }

    internal void Held(Guid requestId) => Record("Held", requestId: requestId);
    internal void Release(bool completed) => Record("Release", completedAtRelease: completed);

    internal void Completed(Result<CommitReceipt> actual)
    {
        var problem = actual.Problem;
        var code = Enum.TryParse<ErrorCode>(problem?.ErrorCode, out var parsed) && Enum.IsDefined(parsed)
            && parsed.ToString() == problem?.ErrorCode ? parsed.ToString() : null;
        Record("TypedResultObserved", problem?.StatusCode, errorCode: code, safeDetail: ClosedDetail(problem?.Detail));
    }

    internal void WriteAndThrow(Exception original)
    {
        var failures = new List<Exception> { original };
        lock (gate)
        {
            ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                kind = Kind,
                saturated,
                observations
            })), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var selectedSend = IsSelected(request);
        if (selectedSend)
        {
            Record("SendStarted", tokenCancelled: token.IsCancellationRequested);
        }
        try
        {
            var response = await base.SendAsync(request, token).ConfigureAwait(false);
            if (selectedSend)
            {
                Record("ResponseHeaders", (int)response.StatusCode, RequestId(response),
                    tokenCancelled: token.IsCancellationRequested);
            }
            return response;
        }
        catch (Exception error)
        {
            if (selectedSend)
            {
                Record("TransportFailure", error is HttpRequestException { StatusCode: { } status } ? (int)status : null,
                    category: Category(error), tokenCancelled: token.IsCancellationRequested);
            }
            throw;
        }
    }

    private bool IsSelected(HttpRequestMessage request)
    {
        lock (gate)
        {
            if (selected == Guid.Empty || !request.Headers.TryGetValues(CommandHeader, out var values))
            {
                return false;
            }
            var headers = values.Take(2).ToArray();
            return headers.Length == 1 && Guid.TryParse(headers[0], out var command) && command == selected;
        }
    }

    private void Record(string phase, int? status = null, Guid? requestId = null, string? errorCode = null,
        string? safeDetail = null, bool? completedAtRelease = null, string? category = null, bool? tokenCancelled = null)
    {
        lock (gate)
        {
            if (observations.Count == MaximumObservations)
            {
                saturated = true;
                return;
            }
            observations.Add(new
            {
                phase,
                elapsedMilliseconds = clock.GetElapsedTime(started).TotalMilliseconds,
                status,
                requestId,
                errorCode,
                safeDetail,
                sdkCompletedAtRelease = completedAtRelease,
                category,
                tokenCancelled
            });
        }
    }

    private static Guid? RequestId(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(McpCallerProtocol.RequestHeader, out var values))
        {
            return null;
        }
        var headers = values.Take(2).ToArray();
        return headers.Length == 1 && headers[0].Length <= 36 && Guid.TryParse(headers[0], out var requestId)
            ? requestId : null;
    }

    private static string Category(Exception error) => error switch
    {
        OperationCanceledException => "Cancellation",
        HttpRequestException => "HttpRequest",
        IOException => "Io",
        _ => "Other"
    };

    private static string? ClosedDetail(string? detail) => detail switch
    {
        "The server response is unavailable." or "The write response is unavailable. Retry the same command ID."
            or "The credential is unavailable or expired." or "The write outcome is unknown. Retry the same command ID."
            or "The database request could not complete. Retry the same command ID for writes."
            or "The internal request has an invalid scope, key, expiry or operation." => detail,
        _ => null
    };
}
