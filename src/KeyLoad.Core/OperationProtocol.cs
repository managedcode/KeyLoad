using KeyLoad.Core.Features.InternalSerialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MismatchedEnvelopeRequestMessage = "The envelope and request IDs differ.";
    private static string CommandFingerprint(ReplicatedOperation operation)
        => JsonData.Fingerprint(new { operation.Id, operation.Kind, operation.PrincipalId, operation.PayloadJson });
    private static T Payload<T>(ReplicatedOperation operation) => NativeCommandPayload.Read<T>(operation);
    private static OperationResult Result<T>(T value) => new(null) { NativeValue = value };
    private static void RequireEnvelopeId(ReplicatedOperation operation, Guid id)
    {
        if (id != operation.Id)
        {
            throw Errors.Fail(ErrorCode.Validation, MismatchedEnvelopeRequestMessage);
        }
    }
}
