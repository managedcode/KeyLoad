using KeyLoad.InternalSerialization;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCommandContentIdentity operations against the caller's database context.</summary>
public static class DatabaseCommandContentIdentity
{
    internal static void RequireOriginalCommandContent(Command<DatabaseCommand> operation, StoredOutcome outcome)
    {
        var original = outcome.PreparedCommand;
        var input = operation.Value!;
        if (original.Value is not { } admitted || admitted.Fingerprint != outcome.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Corruption, CommandExecutionIdentity.InvalidDatabaseCommand);
        }

        if (original.CommandId != operation.CommandId || original.UserId != operation.UserId || admitted.Kind != input.Kind || input.Fingerprint != outcome.Fingerprint)
        {
            throw Errors.Fail(ErrorCode.Conflict, AtomicCommandCommit.CommandContentConflictMessage);
        }

        var type = (DatabaseCommandInputTypes.OperationPayloadType(input.Kind)).Value ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, CommandExecutionValidation.UnsupportedDatabaseCommand);
        var requested = OrleansSerialization.Deserialize(input.Input.Span, type, ValidationProfile.PublicInputElements);
        var retained = OrleansSerialization.Deserialize(admitted.Input.Span, type, ValidationProfile.PublicInputElements);
        if (JsonData.Fingerprint(requested) != JsonData.Fingerprint(retained))
        {
            throw Errors.Fail(ErrorCode.Conflict, AtomicCommandCommit.CommandContentConflictMessage);
        }
    }
}
