using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.Orleans;

internal static class ReplicaOperationClassification
{
    private enum Field { Id, Kind, Principal, EvaluatedAt, Payload }
    private static readonly string[] Fields =
    [
        ReplicaPayloadReader.Name(nameof(ReplicatedOperation.Id)),
        ReplicaPayloadReader.Name(nameof(ReplicatedOperation.Kind)),
        ReplicaPayloadReader.Name(nameof(ReplicatedOperation.PrincipalId)),
        ReplicaPayloadReader.Name(nameof(ReplicatedOperation.EvaluatedAt)),
        ReplicaPayloadReader.Name(nameof(ReplicatedOperation.PayloadJson))
    ];

    internal static bool IsControl(ref Utf8JsonReader reader, int maximumControlPayloadBytes)
    {
        ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.StartObject);
        ulong seen = 0;
        var control = false;
        var payloadBytes = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var field = (Field)ReplicaPayloadReader.Field(ref reader, Fields, ref seen);
            switch (field)
            {
                case Field.Id:
                    ReplicaPayloadReader.Identifier(ref reader);
                    break;
                case Field.Kind:
                    control = CommandAdmissionGovernor.IsControl(ReplicaPayloadReader.Kind(ref reader));
                    break;
                case Field.Principal:
                    ReplicaPayloadReader.Identity(ref reader);
                    break;
                case Field.EvaluatedAt:
                    ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.String && reader.TryGetDateTimeOffset(out _));
                    break;
                case Field.Payload:
                    payloadBytes = ReplicaBase64Token.Length(ref reader);
                    break;
            }
        }

        ReplicaPayloadReader.CompleteObject(ref reader, Fields, seen);
        if (control && payloadBytes > maximumControlPayloadBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.PayloadExceeded);
        }

        return control;
    }
}
