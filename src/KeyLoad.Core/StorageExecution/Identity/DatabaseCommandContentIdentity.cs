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

        var requested = DatabaseCommandInputTypes.Require(input.Kind, input.Input);
        var retained = DatabaseCommandInputTypes.Require(admitted.Kind, admitted.Input);
        if (JsonData.Fingerprint(requested) != JsonData.Fingerprint(retained))
        {
            throw Errors.Fail(ErrorCode.Conflict, AtomicCommandCommit.CommandContentConflictMessage);
        }
    }
}
