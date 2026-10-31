using KeyLoad.Core.CommandOutcomes.Contracts;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.CommandOutcomes.Identity;

internal static class CommandOutcomePartitionIdentity
{
    internal static CommandOutcomePartitionScope Resolve(Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.Value?.Input is not { } input || !DatabaseCommandInputTypes.TryFind(operation.Value.Kind, out var type)
            || input.GetType() != type)
        {
            return CommandOutcomePartitionScope.Unknown;
        }
        var target = CommandPartitions.Of(operation.Value.Kind, input);
        return target.Route switch
        {
            CommandRoute.Partition => Partition(target.Partition),
            CommandRoute.Catalog or CommandRoute.Authorization => CommandOutcomePartitionScope.Global,
            _ => CommandOutcomePartitionScope.Unknown
        };
    }

    private static CommandOutcomePartitionScope Partition(PartitionRef? value)
    {
        if (value is null)
        {
            return CommandOutcomePartitionScope.Unknown;
        }
        try
        {
            EngineResourceAccess.ValidatePartition(value);
            return new(CommandOutcomeScopeKind.Partition, value);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Validation)
        {
            return CommandOutcomePartitionScope.Unknown;
        }
    }
}
