using System.Text.Json;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaSenderValidator
{
    public static void Validate(ReplicaRpc method, ReadOnlySpan<byte> payload, string sender)
    {
        var field = method switch
        {
            ReplicaRpc.RequestVote => ReplicaTransportProtocol.CandidateIdField,
            ReplicaRpc.Append or ReplicaRpc.SnapshotBegin or ReplicaRpc.SnapshotChunk or ReplicaRpc.SnapshotComplete
                => ReplicaTransportProtocol.LeaderIdField,
            ReplicaRpc.Forward or ReplicaRpc.ReadBarrier => null,
            _ => throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload)
        };
        if (field is null)
        {
            return;
        }

        try
        {
            ValidateDeclaredSender(payload, field, sender);
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }

    private static void ValidateDeclaredSender(ReadOnlySpan<byte> payload, string field, string sender)
    {
        var reader = new Utf8JsonReader(payload, new JsonReaderOptions { MaxDepth = JsonDefaults.Options.MaxDepth });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }

        var found = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
            }

            var identity = reader.ValueTextEquals(field);
            if (!reader.Read())
            {
                throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
            }

            if (identity)
            {
                if (found || reader.TokenType != JsonTokenType.String || !reader.ValueTextEquals(sender))
                {
                    throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaTransportProtocol.SenderMismatch);
                }

                found = true;
            }

            reader.Skip();
        }

        if (!found || reader.TokenType != JsonTokenType.EndObject || reader.Read())
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidPayload);
        }
    }
}
