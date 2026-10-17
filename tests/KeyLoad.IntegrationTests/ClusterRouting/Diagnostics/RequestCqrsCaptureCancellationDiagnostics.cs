using System.Text.Json;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Preserves the original canceled wait and emits only closed native observation counters.</summary>
internal static class RequestCqrsCaptureCancellationDiagnostics
{
    private const int Version = 1;
    private const string Kind = "NativeMcpCaptureWaitCanceled";
    private const string OtherNode = "Other";

    internal static void WriteAndThrow(OperationCanceledException original,
        RequestCqrsNativeCaptureSnapshot snapshot, McpTransportStage stage, McpTransportMethodCategory method)
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
        RequestCqrsRf3Protocol.Node1 => RequestCqrsRf3Protocol.Node1,
        RequestCqrsRf3Protocol.Node2 => RequestCqrsRf3Protocol.Node2,
        RequestCqrsRf3Protocol.Node3 => RequestCqrsRf3Protocol.Node3,
        _ => OtherNode
    };
}
