using System.Text.Json;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsReplicaSnapshotJson
{
    internal const string Kind = "replica-snapshot-readiness";
    internal const string Prefix = "snapshot-readiness-";
    private const string Separator = "-";
    private const long NoPosition = 0;
    private const int IdentityParts = 2;
    private const int ArmPart = 0;
    private const int RequestPart = 1;
    private static readonly string[] Fields =
    [nameof(RequestCqrsReplicaSnapshotRecord.Version), nameof(RequestCqrsReplicaSnapshotRecord.Kind),
        nameof(RequestCqrsReplicaSnapshotRecord.SessionId), nameof(RequestCqrsReplicaSnapshotRecord.ArmId),
        nameof(RequestCqrsReplicaSnapshotRecord.RequestId), nameof(RequestCqrsReplicaSnapshotRecord.SnapshotIndex),
        nameof(RequestCqrsReplicaSnapshotRecord.MaterializedPosition), nameof(RequestCqrsReplicaSnapshotRecord.CommittedIndex),
        nameof(RequestCqrsReplicaSnapshotRecord.LastIndex), nameof(RequestCqrsReplicaSnapshotRecord.HasCheckpointPrefix),
        nameof(RequestCqrsReplicaSnapshotRecord.CheckpointPending)];

    internal static RequestCqrsReplicaSnapshotRecord Read(RequestCqrsProbeJson json, ReadOnlySpan<byte> bytes)
    {
        var value = json.Read(bytes, Fields, json.ReplicaSnapshotTypeInfo);
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != Kind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId)
            || value.ArmId == Guid.Empty || value.RequestId == Guid.Empty
            || value.SnapshotIndex < NoPosition || value.MaterializedPosition < NoPosition
            || value.CommittedIndex < value.SnapshotIndex || value.LastIndex < value.CommittedIndex
            || value.HasCheckpointPrefix || value.CheckpointPending)
        {
            throw Invalid();
        }
        return value;
    }

    internal static byte[] Write(RequestCqrsProbeJson json, RequestCqrsReplicaSnapshotRecord value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, json.ReplicaSnapshotTypeInfo);
        _ = Read(json, bytes);
        return bytes;
    }

    internal static string Name(RequestCqrsReplicaSnapshotRecord value)
        => Prefix + value.ArmId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + Separator
            + value.RequestId.ToString(RequestCqrsProbeProtocol.SessionIdFormat) + RequestCqrsProbeProtocol.JsonFileSuffix;

    internal static bool IsName(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal)
            || !name.EndsWith(RequestCqrsProbeProtocol.JsonFileSuffix, StringComparison.Ordinal))
        {
            return false;
        }
        var parts = name[Prefix.Length..^RequestCqrsProbeProtocol.JsonFileSuffix.Length].Split(Separator);
        return parts.Length == IdentityParts
            && Guid.TryParseExact(parts[ArmPart], RequestCqrsProbeProtocol.SessionIdFormat, out var arm)
            && arm != Guid.Empty && arm.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == parts[ArmPart]
            && Guid.TryParseExact(parts[RequestPart], RequestCqrsProbeProtocol.SessionIdFormat, out var request)
            && request != Guid.Empty && request.ToString(RequestCqrsProbeProtocol.SessionIdFormat) == parts[RequestPart];
    }

    internal static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidRecord);
}
