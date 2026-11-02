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
            && (SameInput(retained, operation.Value!.Kind, operation.Value.Input /* KLD0041: The existing persisted command slot contains the exact runtime input selected and validated by its closed Kind discriminator. */) /* KLD0041: A retry compares the public shape of the Kind-selected native inputs while preserving their stored type and JSON rules. */);

    internal static bool SameInput(Command<DatabaseCommand> retained, OperationKind kind, object /* KLD0041: The native persisted input retains its Kind-selected runtime business contract for exact retry content comparison. */ input)
        => retained.Value is { } original && original.Kind == kind
            && PublicShape(DatabaseCommandInputTypes.Require(original.Kind, original.Input /* KLD0041: The existing persisted command slot contains the exact runtime input selected and validated by its closed Kind discriminator. */) /* KLD0041: The closed operation-kind map validates the exact runtime contract held by the existing persisted input slot. */) /* KLD0041: A retry compares the public shape of the Kind-selected native inputs while preserving their stored type and JSON rules. */.AsSpan()
                .SequenceEqual(PublicShape(DatabaseCommandInputTypes.Require(kind, input) /* KLD0041: The closed operation-kind map validates the exact runtime contract held by the existing persisted input slot. */) /* KLD0041: A retry compares the public shape of the Kind-selected native inputs while preserving their stored type and JSON rules. */);

    // The caller's public input shape; server-attached [JsonIgnore] state is not part of command content.
    private static byte[] PublicShape(object /* KLD0041: The native persisted input retains its Kind-selected runtime business contract for exact retry content comparison. */ input)
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(input, input.GetType() /* KLD0041: This persisted input boundary must verify its exact runtime contract against the closed operation-kind map. */, JsonDefaults.Options) /* KLD0041: Retry identity uses the exact runtime business contract selected by Kind, excluding server-only JsonIgnore facts. */;
}
