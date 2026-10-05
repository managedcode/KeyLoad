using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Execution;

internal readonly record struct CommandOutcomeSelection(byte[] Key, StoredOutcome? Outcome, bool IsLegacy);

internal static class CommandOutcomeKeyResolver
{
    private const string InvalidScopeMessage = "The command outcome scope is invalid.";
    private const string InvalidMetadataMessage = "The command outcome scope or locator is inconsistent.";
    private const string DuplicateOutcomeMessage = "The command outcome exists in multiple identity scopes.";

    internal static CommandOutcomeSelection ForNew(string principalId, Guid commandId,
        CommandOutcomePartitionScope scope)
        => new(KeyFor(principalId, commandId, scope), null, false);

    internal static bool HasLegacyOutcome(IKeyValueView view, string principalId, Guid commandId)
        => view.ReadOwnedValue(KeySpace.LegacyOutcomeKey(principalId, commandId)) is not null;

    internal static CommandOutcomeSelection Select(IKeyValueView view, string principalId, Guid commandId,
        CommandOutcomePartitionScope expected)
    {
        var scopedKey = KeyFor(principalId, commandId, expected);
        var scoped = view.GetRecord<StoredOutcome>(scopedKey);
        var legacyKey = KeySpace.LegacyOutcomeKey(principalId, commandId);
        var legacy = view.GetRecord<StoredOutcome>(legacyKey);

        if (expected.Kind == CommandOutcomeScopeKind.Partition && expected.Partition is { } partition)
        {
            ValidateOrphanLocator(view, scoped, partition, principalId, commandId, scoped: true);
            ValidateOrphanLocator(view, legacy, partition, principalId, commandId, scoped: false);
        }
        if (legacy is not null)
        {
            ValidateLegacy(view, legacy, principalId, commandId);
        }
        if (scoped is not null)
        {
            ValidateScoped(view, scoped, expected, principalId, commandId);
        }
        if (legacy is not null && scoped is not null)
        {
            if (legacy.ScopeKind == CommandOutcomeScopeKind.Unknown || SameScope(legacy, scoped))
            {
                throw Errors.Fail(ErrorCode.Corruption, DuplicateOutcomeMessage);
            }
        }

        if (legacy is not null && (legacy.ScopeKind == CommandOutcomeScopeKind.Unknown || SameScope(legacy, expected)))
        {
            return new(legacyKey, legacy, true);
        }
        return new(scopedKey, scoped, false);
    }

    internal static void ValidateSelectedScope(IKeyValueView view, ReplicatedOperation operation,
        CommandOutcomeSelection selection)
    {
        if (selection.Outcome is not { } outcome)
        {
            return;
        }

        var expected = CommandOutcomePartitionIdentity.Resolve(operation);
        if (outcome.ScopeKind == CommandOutcomeScopeKind.Unknown && outcome.Partition is null && selection.IsLegacy)
        {
            return;
        }
        if (!SameScope(outcome, expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
        if (selection.IsLegacy)
        {
            ValidateLegacy(view, outcome, operation.PrincipalId, operation.Id);
        }
        else
        {
            ValidateScoped(view, outcome, expected, operation.PrincipalId, operation.Id);
        }
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
        string principalId, Guid commandId, bool scoped)
    {
        var key = scoped
            ? CommandOutcomePartitionLocatorSerialization.ScopedKey(partition, principalId, commandId)
            : CommandOutcomePartitionLocatorSerialization.LegacyKey(partition, principalId, commandId);
        if (view.ReadOwnedValue(key) is not null
            && (outcome is null || outcome.ScopeKind != CommandOutcomeScopeKind.Partition
                || outcome.Partition != partition))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
    }

    private static void ValidateLegacy(IKeyValueView view, StoredOutcome outcome, string principalId, Guid commandId)
    {
        var valid = outcome.ScopeKind switch
        {
            CommandOutcomeScopeKind.Unknown or CommandOutcomeScopeKind.Global => outcome.Partition is null,
            CommandOutcomeScopeKind.Partition => outcome.Partition is not null
                && CommandOutcomePartitionLocatorSerialization.MatchesLegacy(view, outcome.Partition, principalId, commandId),
            _ => false
        };
        if (!valid)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
    }

    private static void ValidateScoped(IKeyValueView view, StoredOutcome outcome, CommandOutcomePartitionScope expected,
        string principalId, Guid commandId)
    {
        if (!SameScope(outcome, expected))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
        if (expected.Kind == CommandOutcomeScopeKind.Partition && expected.Partition is not null
            && !CommandOutcomePartitionLocatorSerialization.MatchesScoped(view, expected.Partition, principalId, commandId))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidMetadataMessage);
        }
    }

    private static bool SameScope(StoredOutcome outcome, CommandOutcomePartitionScope scope)
        => outcome.ScopeKind == scope.Kind && outcome.Partition == scope.Partition;

    private static bool SameScope(StoredOutcome left, StoredOutcome right)
        => left.ScopeKind == right.ScopeKind && left.Partition == right.Partition;
}
