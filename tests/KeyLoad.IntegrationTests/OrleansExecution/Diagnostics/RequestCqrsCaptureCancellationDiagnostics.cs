using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.OrleansExecution;

/// <summary>Preserves the original canceled wait and emits only closed execution observation counters.</summary>
internal static class RequestCqrsCaptureCancellationDiagnostics
{
    private const int Version = 1;
    private const string Kind = "McpCaptureWaitCanceled";
    private const string OtherNode = "Other";

    internal static void WriteAndThrow(OperationCanceledException original,
        RequestCqrsCaptureSnapshot snapshot, McpTransportStage stage, McpTransportMethodCategory method)
    {
        var failures = new List<Exception> { original };
        ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
        {
            schemaVersion = Version,
            kind = Kind,
            node = ClosedNode(snapshot.Node),
            expectedStage = stage.ToString(),
            expectedMethod = method.ToString(),
            snapshot.Lines,
            snapshot.Candidates,
            snapshot.Accepted,
            snapshot.Malformed,
            snapshot.Oversized,
            snapshot.Saturated
        })), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string ClosedNode(string node) => node switch
    {
        RequestCqrsClusterProtocol.Node1 => RequestCqrsClusterProtocol.Node1,
        RequestCqrsClusterProtocol.Node2 => RequestCqrsClusterProtocol.Node2,
        RequestCqrsClusterProtocol.Node3 => RequestCqrsClusterProtocol.Node3,
        _ => OtherNode
    };
}
