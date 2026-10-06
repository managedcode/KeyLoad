using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaReplayClassifier
{
    private const int VoteTermValidationBoundary = 0;
    private const int VoteLastIndexValidationBoundary = 0;
    private const int VoteLastTermValidationBoundary = 0;
    private const int VoteEmptyLastIndex = 0;
    private const int VoteEmptyLastTerm = 0;

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
        const int EmptyUtf8Length = 0;

        ReplicaNativeAdmissionPolicy.Require(payload.Length <= ReplicaTransportProtocol.MaximumMetadataBytes);
        var inspected = ReplicaNativeAdmissionPolicy.Inspect<string>(payload, configuration.MaxAppendEntries);
        ReplicaNativeAdmissionPolicy.Require(inspected.Utf8Length(inspected.Value) == EmptyUtf8Length);
        return method == ReplicaRpc.ControlReadBarrier ? ReplicaReplayPool.Critical : ReplicaReplayPool.ReadBarrier;
    }

    private static ReplicaReplayPool Critical(ReplicaRpc method, ReadOnlyMemory<byte> payload, ReplicaConfiguration configuration)
    {
        const int TermValidationBoundary = 0;
        const int OffsetValidationBoundary = 0;
        const int BytesLengthValidationBoundary = 0;

        if (method == ReplicaRpc.SnapshotChunk)
        {
            var inspected = ReplicaNativeAdmissionPolicy.Inspect<SnapshotChunkRequest>(payload, configuration.MaxAppendEntries);
            var chunk = inspected.Value;
            ReplicaNativeAdmissionPolicy.Require(chunk.Term > TermValidationBoundary && chunk.TransferId != Guid.Empty && chunk.Offset >= OffsetValidationBoundary
                && chunk.Bytes.Length > BytesLengthValidationBoundary && chunk.Bytes.Length <= configuration.SnapshotChunkBytes);
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
                ReplicaNativeAdmissionPolicy.Require(complete.Term > TermValidationBoundary && complete.TransferId != Guid.Empty);
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
        return ReplicaReplayPool.Critical;
    }

    private static void Vote(VoteRequest request)
        => ReplicaNativeAdmissionPolicy.Require(request.Term > VoteTermValidationBoundary && request.LastIndex >= VoteLastIndexValidationBoundary && request.LastTerm >= VoteLastTermValidationBoundary
            && request.LastTerm <= request.Term && (request.LastIndex == VoteEmptyLastIndex) == (request.LastTerm == VoteEmptyLastTerm));

    private static void Begin(ReplicaInspectedValue<SnapshotBeginRequest> inspected, ReplicaConfiguration configuration)
    {
        const int TermValidationBoundary = 0;
        const int IndexValidationBoundary = 0;
        const int SnapshotLengthValidationBoundary = 0;

        var begin = inspected.Value;
        var snapshot = begin.Snapshot;
        ReplicaNativeAdmissionPolicy.Require(begin.Term > TermValidationBoundary && snapshot is not null);
        var sha = inspected.Metadata(snapshot.Sha256, ReplicaTransportProtocol.HashHexCharacters);
        var name = inspected.Metadata(snapshot.FileName, ReplicaTransportProtocol.MaximumMetadataBytes);
        ReplicaNativeAdmissionPolicy.Require(snapshot.TransferId != Guid.Empty && snapshot.Incarnation == configuration.Incarnation
            && snapshot.Index > IndexValidationBoundary && snapshot.Term > TermValidationBoundary && snapshot.Term <= begin.Term
            && snapshot.Length > SnapshotLengthValidationBoundary && snapshot.Length <= configuration.MaxSnapshotBytes
            && sha.Length == ReplicaTransportProtocol.HashHexCharacters && sha.All(Uri.IsHexDigit)
            && name == snapshot.TransferId.ToString(ReplicaTransportProtocol.NonceFormat) + ReplicaProtocol.SnapshotExtension);
    }
}
