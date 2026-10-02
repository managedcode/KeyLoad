using System.Text.Json;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MismatchedEnvelopeRequestMessage = "The envelope and request IDs differ.";
    private static string CommandFingerprint(ReplicatedOperation operation)
        => JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson });
    private static T Payload<T>(ReplicatedOperation operation) => JsonDefaults.Deserialize<T>(operation.PayloadJson);
    private static OperationResult Result<T>(T value) => new(JsonSerializer.Serialize(value, JsonDefaults.Options));
    private static void RequireEnvelopeId(ReplicatedOperation operation, Guid id)
    {
        if (id != operation.Id)
        {
            throw Errors.Fail(ErrorCode.Validation, MismatchedEnvelopeRequestMessage);
        }
    }
}
