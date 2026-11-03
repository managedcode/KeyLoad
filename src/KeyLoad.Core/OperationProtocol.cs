using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes OperationProtocol operations against the caller's database context.</summary>
public static class OperationProtocol
{
    internal const string MismatchedEnvelopeRequestMessage = "The envelope and request IDs differ.";
    internal static T Payload<T>(Command<DatabaseCommand> operation) => operation.Value!.Input /* KLD0041: The existing persisted command slot contains the exact runtime input selected and validated by its closed Kind discriminator. */ is T input /* KLD0042: The persisted command Kind selects the exact input type extracted before typed execution. */
        ? input : throw Errors.Fail(ErrorCode.Corruption, KeyLoad.InternalSerialization.RuntimePayloadContract.InvalidPayload);
    internal static Result<object> /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */ Result<T>(T value) => ManagedCode.Communication.Result<object>.Succeed(value /* KLD0042: The existing persisted outcome schema stores a native object payload; value-type business outcomes are boxed at this single storage boundary. */!) /* KLD0041: The existing native stored-outcome schema carries different operation payload types; this step preserves that exact persisted result. */;
    internal static void RequireEnvelopeId(Command<DatabaseCommand> operation, Guid id)
    {
        if (id != operation.CommandId)
        {
            throw Errors.Fail(ErrorCode.Validation, MismatchedEnvelopeRequestMessage);
        }
    }
}
