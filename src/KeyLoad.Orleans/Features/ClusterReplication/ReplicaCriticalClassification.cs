using System.Text.Json;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaCriticalClassification
{
    private enum ChunkField { Leader, Term, TransferId, Offset, Bytes }
    private static readonly JsonSerializerOptions Options = new(JsonDefaults.Options) { AllowDuplicateProperties = false };
    private static readonly string[] ChunkFields =
    [
        ReplicaTransportProtocol.LeaderIdField, ReplicaPayloadReader.Name(nameof(SnapshotChunkRequest.Term)),
        ReplicaPayloadReader.Name(nameof(SnapshotChunkRequest.TransferId)), ReplicaPayloadReader.Name(nameof(SnapshotChunkRequest.Offset)),
        ReplicaPayloadReader.Name(nameof(SnapshotChunkRequest.Bytes))
    ];

    internal static void Validate(ReplicaRpc method, ReadOnlySpan<byte> payload, ReplicaConfiguration configuration)
    {
        if (method == ReplicaRpc.SnapshotChunk)
        {
            Chunk(payload, configuration);
            return;
        }

        ReplicaPayloadReader.Require(payload.Length <= ReplicaTransportProtocol.MaximumMetadataBytes);
        switch (method)
        {
            case ReplicaRpc.RequestVote:
                Vote(JsonSerializer.Deserialize<VoteRequest>(payload, Options));
                break;
            case ReplicaRpc.SnapshotBegin:
                Begin(JsonSerializer.Deserialize<SnapshotBeginRequest>(payload, Options), configuration);
                break;
            case ReplicaRpc.SnapshotComplete:
                Complete(JsonSerializer.Deserialize<SnapshotCompleteRequest>(payload, Options));
                break;
            default:
                throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }

    private static void Vote(VoteRequest? request)
    {
        ReplicaPayloadReader.Require(request is not null);
        ReplicaPayloadReader.Require(request!.Term > 0 && request.LastIndex >= 0 && request.LastTerm >= 0
            && request.LastTerm <= request.Term && (request.LastIndex == 0) == (request.LastTerm == 0));
    }

    private static void Begin(SnapshotBeginRequest? request, ReplicaConfiguration configuration)
    {
        ReplicaPayloadReader.Require(request is not null && request.Term > 0 && request.Snapshot is not null);
        var snapshot = request!.Snapshot;
        ReplicaPayloadReader.Require(snapshot.TransferId != Guid.Empty && snapshot.Incarnation == configuration.Incarnation
            && snapshot.Index > 0 && snapshot.Term > 0 && snapshot.Term <= request.Term
            && snapshot.Length > 0 && snapshot.Length <= configuration.MaxSnapshotBytes
            && snapshot.Sha256 is { Length: ReplicaTransportProtocol.HashHexCharacters } && snapshot.Sha256.All(Uri.IsHexDigit)
            && snapshot.FileName == snapshot.TransferId.ToString(ReplicaTransportProtocol.NonceFormat) + ReplicaProtocol.SnapshotExtension);
    }

    private static void Complete(SnapshotCompleteRequest? request)
    {
        ReplicaPayloadReader.Require(request is not null && request.Term > 0 && request.TransferId != Guid.Empty);
    }

    private static void Chunk(ReadOnlySpan<byte> payload, ReplicaConfiguration configuration)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = JsonDefaults.Options.MaxDepth });
        ReplicaPayloadReader.Require(reader.Read() && reader.TokenType == JsonTokenType.StartObject);
        ulong seen = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var field = (ChunkField)ReplicaPayloadReader.Field(ref reader, ChunkFields, ref seen);
            ChunkValue(ref reader, field, configuration);
        }

        ReplicaPayloadReader.CompleteObject(ref reader, ChunkFields, seen);
        ReplicaPayloadReader.Require(!reader.Read());
    }

    private static void ChunkValue(ref Utf8JsonReader reader, ChunkField field, ReplicaConfiguration configuration)
    {
        switch (field)
        {
            case ChunkField.Leader:
                ReplicaPayloadReader.Identity(ref reader);
                break;
            case ChunkField.Term:
                ReplicaPayloadReader.Number(ref reader, positive: true);
                break;
            case ChunkField.TransferId:
                ReplicaPayloadReader.Identifier(ref reader);
                break;
            case ChunkField.Offset:
                ReplicaPayloadReader.Number(ref reader);
                break;
            case ChunkField.Bytes:
                var length = ReplicaBase64Token.Length(ref reader);
                ReplicaPayloadReader.Require(length > 0 && length <= configuration.SnapshotChunkBytes);
                break;
        }
    }
}
