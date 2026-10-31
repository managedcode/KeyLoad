using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes OperationProtocol operations against the caller's database context.</summary>
public static class OperationProtocol
{
    internal const string MismatchedEnvelopeRequestMessage = "The envelope and request IDs differ.";
    internal static string CommandFingerprint(Command<DatabaseCommand> operation) => operation.Value!.Fingerprint;
    internal static T Payload<T>(Command<DatabaseCommand> operation) => operation.Value!.Input is T input
        ? input : throw Errors.Fail(ErrorCode.Corruption, KeyLoad.InternalSerialization.PayloadVersion.InvalidPayload);
    internal static Result<object> Result<T>(T value) => ManagedCode.Communication.Result<object>.Succeed(value!);
    internal static void RequireEnvelopeId(Command<DatabaseCommand> operation, Guid id)
    {
        if (id != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, MismatchedEnvelopeRequestMessage);
        }
    }
}
