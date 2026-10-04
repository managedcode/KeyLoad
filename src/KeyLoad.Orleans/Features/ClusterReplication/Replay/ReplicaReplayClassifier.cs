using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaReplayClassifier
{
    internal static ReplicaReplayPool Classify(ReplicaRpc method, ReadOnlyMemory<byte> payload, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes, DatabaseEngine? canonicalDatabase)
    {
        try
        {
            return method switch
            {
                ReplicaRpc.Forward => Forward(payload, configuration, maximumControlPayloadBytes, canonicalDatabase),
                ReplicaRpc.Append or ReplicaRpc.ReadProbe => ReplicaAppendClassification.Classify(
                    ReplicaNativeAdmissionPolicy.Inspect<AppendRequest>(payload, configuration.MaxAppendEntries),
                    configuration, maximumControlPayloadBytes, method == ReplicaRpc.ReadProbe, canonicalDatabase),
                ReplicaRpc.ReadBarrier or ReplicaRpc.ControlReadBarrier => Read(method, payload, configuration),
                _ => Critical(method, payload, configuration)
            };
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }

    private static ReplicaReplayPool Forward(ReadOnlyMemory<byte> payload, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes, DatabaseEngine? canonicalDatabase)
    {
        var inspected = ReplicaNativeAdmissionPolicy.Inspect<ReplicatedOperation>(payload, configuration.MaxAppendEntries);
        return ReplicaNativeOperationAdmission.Validate(inspected, inspected.Value, canonicalDatabase, maximumControlPayloadBytes)
            ? ReplicaReplayPool.Critical : ReplicaReplayPool.Forward;
    }

    private static ReplicaReplayPool Read(ReplicaRpc method, ReadOnlyMemory<byte> payload, ReplicaConfiguration configuration)
    {
        ReplicaNativeAdmissionPolicy.Require(payload.Length <= ReplicaTransportProtocol.MaximumMetadataBytes);
        var inspected = ReplicaNativeAdmissionPolicy.Inspect<string>(payload, configuration.MaxAppendEntries);
        ReplicaNativeAdmissionPolicy.Require(inspected.Utf8Length(inspected.Value) == 0);
        return method == ReplicaRpc.ControlReadBarrier ? ReplicaReplayPool.Critical : ReplicaReplayPool.ReadBarrier;
    }

    private static ReplicaReplayPool Critical(ReplicaRpc method, ReadOnlyMemory<byte> payload, ReplicaConfiguration configuration)
    {
        if (method == ReplicaRpc.SnapshotChunk)
        {
            var inspected = ReplicaNativeAdmissionPolicy.Inspect<SnapshotChunkRequest>(payload, configuration.MaxAppendEntries);
            var chunk = inspected.Value;
            ReplicaNativeAdmissionPolicy.Require(chunk.Term > 0 && chunk.TransferId != Guid.Empty && chunk.Offset >= 0
                && chunk.Bytes.Length > 0 && chunk.Bytes.Length <= configuration.SnapshotChunkBytes);
            return ReplicaReplayPool.Critical;
        }
        ReplicaNativeAdmissionPolicy.Require(payload.Length <= ReplicaTransportProtocol.MaximumMetadataBytes);
        switch (method)
        {
            case ReplicaRpc.RequestVote:
                Vote(ReplicaNativeAdmissionPolicy.Inspect<VoteRequest>(payload, configuration.MaxAppendEntries).Value);
                break;
            case ReplicaRpc.SnapshotBegin:
                Begin(ReplicaNativeAdmissionPolicy.Inspect<SnapshotBeginRequest>(payload, configuration.MaxAppendEntries), configuration);
                break;
            case ReplicaRpc.SnapshotComplete:
                var complete = ReplicaNativeAdmissionPolicy.Inspect<SnapshotCompleteRequest>(payload, configuration.MaxAppendEntries).Value;
                ReplicaNativeAdmissionPolicy.Require(complete.Term > 0 && complete.TransferId != Guid.Empty);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
        return ReplicaReplayPool.Critical;
    }

    private static void Vote(VoteRequest request)
        => ReplicaNativeAdmissionPolicy.Require(request.Term > 0 && request.LastIndex >= 0 && request.LastTerm >= 0
            && request.LastTerm <= request.Term && (request.LastIndex == 0) == (request.LastTerm == 0));

    private static void Begin(ReplicaInspectedValue<SnapshotBeginRequest> inspected, ReplicaConfiguration configuration)
    {
        var begin = inspected.Value;
        var snapshot = begin.Snapshot;
        ReplicaNativeAdmissionPolicy.Require(begin.Term > 0 && snapshot is not null);
        var sha = inspected.Metadata(snapshot.Sha256, ReplicaTransportProtocol.HashHexCharacters);
        var name = inspected.Metadata(snapshot.FileName, ReplicaTransportProtocol.MaximumMetadataBytes);
        ReplicaNativeAdmissionPolicy.Require(snapshot.TransferId != Guid.Empty && snapshot.Incarnation == configuration.Incarnation
            && snapshot.Index > 0 && snapshot.Term > 0 && snapshot.Term <= begin.Term
            && snapshot.Length > 0 && snapshot.Length <= configuration.MaxSnapshotBytes
            && sha.Length == ReplicaTransportProtocol.HashHexCharacters && sha.All(Uri.IsHexDigit)
            && name == snapshot.TransferId.ToString(ReplicaTransportProtocol.NonceFormat) + ReplicaProtocol.SnapshotExtension);
    }
}
