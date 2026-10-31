using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCommandContentIdentity operations against the caller's database context.</summary>
public static class DatabaseCommandContentIdentity
{
    internal static void RequireOriginalCommandContent(Command<DatabaseCommand> operation, StoredOutcome outcome)
    {
        if (outcome.PreparedCommand.Value is null)
        {
            throw Errors.Fail(ErrorCode.Corruption, CommandExecutionIdentity.InvalidDatabaseCommand);
        }

        if (!SameContent(outcome.PreparedCommand, operation))
        {
            throw Errors.Fail(ErrorCode.Conflict, AtomicCommandCommit.CommandContentConflictMessage);
        }
    }

    /// <summary>Compares a retried command with the retained original; only a retry of a stored command reaches it.</summary>
    internal static bool SameContent(Command<DatabaseCommand> retained, Command<DatabaseCommand> operation)
        => retained.CommandId == operation.CommandId && retained.UserId == operation.UserId
            && SameInput(retained, operation.Value!.Kind, operation.Value.Input);

    internal static bool SameInput(Command<DatabaseCommand> retained, OperationKind kind, object input)
        => retained.Value is { } original && original.Kind == kind
            && PublicShape(DatabaseCommandInputTypes.Require(original.Kind, original.Input)).AsSpan()
                .SequenceEqual(PublicShape(DatabaseCommandInputTypes.Require(kind, input)));

    // The caller's public input shape; server-attached [JsonIgnore] state is not part of command content.
    private static byte[] PublicShape(object input)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(input, input.GetType(), JsonDefaults.Options);
}
