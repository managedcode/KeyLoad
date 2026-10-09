using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Passively records bounded HTTP completion facts during native initialization only.</summary>
internal sealed class McpInitializeHttpObservation(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private const int MaximumObservations = 8;
    private const int SchemaVersion = 1;
    private const int NoResponse = 0;
    private const string Kind = "NativeMcpInitializeHttp";
    private const string NoneCategory = "None";
    private const string CanceledCategory = "Canceled";
    private const string HttpCategory = "HttpRequest";
    private const string IoCategory = "Io";
    private const string OtherCategory = "Other";
    private const string PostMethod = "Post";
    private const string GetMethod = "Get";
    private const string Node1Name = "Node1";
    private const string Node2Name = "Node2";
    private const string Node3Name = "Node3";
    private readonly Lock gate = new();
    private readonly List<object> observations = [];
    private bool enabled = true;
    private bool saturated;

    /// <summary>Stops recording before later native caller operations.</summary>
    internal void Stop()
    {
        lock (gate)
        { enabled = false; }
    }

    /// <summary>Preserves the initiating native exception and bounded diagnostic write failures.</summary>
    internal void WriteAndThrow(Exception original, string node)
    {
        var failures = new List<Exception> { original };
        lock (gate)
        {
            ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                kind = Kind,
                node = ClosedNode(node),
                saturated,
                observations
            })), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    /// <summary>Passes the same request, cancellation token and unconsumed response through the native handler.</summary>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var recording = Recording();
        if (recording)
        { Record(request.Method, NoResponse, false, false, false, cancellationToken.IsCancellationRequested, NoneCategory); }
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (recording)
            { Record(request.Method, (int)response.StatusCode, true, true, false, cancellationToken.IsCancellationRequested, NoneCategory); }
            return response;
        }
        catch (Exception error)
        {
            if (recording)
            {
                Record(request.Method, error is HttpRequestException { StatusCode: { } status } ? (int)status : NoResponse, true, false, error is OperationCanceledException,
                cancellationToken.IsCancellationRequested, Category(error));
            }
            throw;
        }
    }

    private bool Recording()
    {
        lock (gate)
        { return enabled; }
    }

    private void Record(HttpMethod method, int status, bool terminal, bool response, bool canceled, bool tokenCanceled, string category)
    {
        lock (gate)
        {
            if (!enabled)
            { return; }
            if (observations.Count == MaximumObservations)
            { saturated = true; return; }
            observations.Add(new
            {
                method = method == HttpMethod.Post ? PostMethod : method == HttpMethod.Get ? GetMethod : OtherCategory,
                status,
                sendStarted = true,
                sendCompleted = terminal,
                responseReceived = response,
                canceled,
                tokenCanceled,
                exceptionKind = category
            });
        }
    }

    private static string Category(Exception error) => error switch
    {
        OperationCanceledException => CanceledCategory,
        HttpRequestException => HttpCategory,
        IOException => IoCategory,
        _ => OtherCategory
    };

    private static string ClosedNode(string node) => node switch
    {
        McpCallerProtocol.Node1 => Node1Name,
        McpCallerProtocol.Node2 => Node2Name,
        McpCallerProtocol.Node3 => Node3Name,
        _ => OtherCategory
    };
}
