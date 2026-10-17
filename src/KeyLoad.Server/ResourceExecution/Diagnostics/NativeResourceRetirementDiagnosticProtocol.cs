using System.Text;
using KeyLoad.Clustering;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal static class NativeResourceRetirementDiagnosticProtocol
{
    internal const string Path = "/api/admin/native-resource-retirement";
    internal const string SignatureHeader = "X-KeyLoad-Native-Resource-Retirement-Signature";
    internal const string ContentType = "application/octet-stream";
    internal const string DiagnosticOperationAlias = "keyload.probe.native-resource-retirement-operation.v1";
    internal const string RequestAlias = "keyload.probe.native-resource-retirement-request.v1";
    internal const string WitnessAlias = "keyload.probe.native-resource-retirement-witness.v1";
    private const string Purpose = "keyload.probe.native-resource-retirement.v1\0";
    private const int Empty = 0;
    private static readonly byte[] PurposeBytes = Encoding.UTF8.GetBytes(Purpose);

    internal static int MaximumPayloadBytes(IOptions<DatabaseLimits> limits)
        => Math.Min(limits.Value.MaxBatchBytes, ReplicaTransportProtocol.MaximumDiscoveryBytes) - PurposeBytes.Length;

    internal static byte[] Frame(ReadOnlySpan<byte> payload, IOptions<DatabaseLimits> limits)
    {
        if (payload.Length <= Empty || payload.Length > MaximumPayloadBytes(limits))
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ResourceRetirementNativeProtocol.Unavailable);
        }
        var frame = new byte[checked(PurposeBytes.Length + payload.Length)];
        PurposeBytes.CopyTo(frame, Empty);
        payload.CopyTo(frame.AsSpan(PurposeBytes.Length));
        return frame;
    }
}
