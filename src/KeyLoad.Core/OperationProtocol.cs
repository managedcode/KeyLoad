using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string MismatchedEnvelopeRequestMessage = "The envelope and request IDs differ.";
    private static string CommandFingerprint(Command<DatabaseCommand> operation) => operation.Value!.Fingerprint;
    internal static T Payload<T>(Command<DatabaseCommand> operation)
        => NativeSerialization.Deserialize<T>(operation.Value!.Input.Span, KeyLoad.Features.InternalSerialization.NativeValidationProfile.PublicInputElements);
    private static Result<object> Result<T>(T value) => ManagedCode.Communication.Result<object>.Succeed(value!);
    private static void RequireEnvelopeId(Command<DatabaseCommand> operation, Guid id)
    {
        if (id != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, MismatchedEnvelopeRequestMessage);
        }
    }
}
