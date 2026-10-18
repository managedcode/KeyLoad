using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal static class CommandOutcomeKeyResolver
{
    private const string InvalidScopeMessage = "The command outcome scope is invalid.";
    private const string InvalidMetadataMessage = "The command outcome scope or locator is inconsistent.";

    internal static CommandOutcomeSelection ForNew(string principalId, Guid commandId,
        CommandOutcomePartitionScope scope)
        => new(KeyFor(principalId, commandId, scope), null);

    internal static bool IsUnstored(IKeyValueView view, string principalId, Guid commandId,
        CommandOutcomePartitionScope scope)
    {
        if (view.ReadOwnedValue(KeyFor(principalId, commandId, scope)) is not null)
        {
            return false;
        }
        return scope.Kind != CommandOutcomeScopeKind.Partition || scope.Partition is null
            || view.ReadOwnedValue(CommandOutcomePartitionLocatorSerialization.ScopedKey(scope.Partition,
                principalId, commandId)) is null;
    }

    internal static CommandOutcomeSelection Select(IKeyValueView view, string principalId, Guid commandId,
        CommandOutcomePartitionScope expected, StorageReadObserver? observer = null)
    {
        var scopedKey = KeyFor(principalId, commandId, expected);
        var scoped = view.GetRecord<StoredOutcome>(scopedKey, observer);

        if (expected.Kind == CommandOutcomeScopeKind.Partition && expected.Partition is { } partition)
        {
            ValidateOrphanLocator(view, scoped, partition, principalId, commandId, observer);
        }
        if (scoped is not null)
        {
            ValidateScoped(view, scoped, expected, principalId, commandId, observer);
        }
        return new(scopedKey, scoped);
    }

    internal static void ValidateSelectedScope(IKeyValueView view, Command<DatabaseCommand> operation,
        CommandOutcomeSelection selection)
    {
        if (selection.Outcome is not { } outcome)
        {
            return;
        }

        var expected = CommandOutcomePartitionIdentity.Resolve(operation);
        ValidateScoped(view, outcome, expected, operation.UserId!, operation.CommandId);
    }

    private static byte[] KeyFor(string principalId, Guid commandId, CommandOutcomePartitionScope scope)
        => scope.Kind switch
        {
            CommandOutcomeScopeKind.Unknown when scope.Partition is null => KeySpace.UnknownOutcome(principalId, commandId),
            CommandOutcomeScopeKind.Global when scope.Partition is null => KeySpace.GlobalOutcome(principalId, commandId),
            CommandOutcomeScopeKind.Partition when scope.Partition is not null
                => KeySpace.PartitionOutcome(scope.Partition, principalId, commandId),
            _ => throw Errors.Fail(ErrorCode.Corruption, InvalidScopeMessage)
        };

    private static void ValidateOrphanLocator(IKeyValueView view, StoredOutcome? outcome, PartitionRef partition,
        string principalId, Guid commandId, StorageReadObserver? observer)
    {
        var key = CommandOutcomePartitionLocatorSerialization.ScopedKey(partition, principalId, commandId);
        if (CommandOutcomePartitionLocatorSerialization.ReadScopedValue(view, key, observer) is not null
            && (outcome is null || outcome.ScopeKind != CommandOutcomeScopeKind.Partition
                || outcome.Partition != partition))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
    }

    private static void ValidateScoped(IKeyValueView view, StoredOutcome outcome, CommandOutcomePartitionScope expected,
        string principalId, Guid commandId, StorageReadObserver? observer = null)
    {
        if (!SameScope(outcome, expected) || outcome.PreparedCommand is not { } original
            || original.CommandId != commandId || original.UserId != principalId)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
        if (expected.Kind == CommandOutcomeScopeKind.Partition && expected.Partition is not null
            && !CommandOutcomePartitionLocatorSerialization.MatchesScoped(view, expected.Partition, principalId, commandId, observer))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
    }

    private static bool SameScope(StoredOutcome outcome, CommandOutcomePartitionScope scope)
        => outcome.ScopeKind == scope.Kind && outcome.Partition == scope.Partition;

}
