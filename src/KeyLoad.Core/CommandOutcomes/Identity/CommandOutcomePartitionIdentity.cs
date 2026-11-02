using KeyLoad.Core.CommandOutcomes.Contracts;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core.CommandOutcomes.Identity;

internal static class CommandOutcomePartitionIdentity
{
    internal static CommandOutcomePartitionScope Resolve(Command<DatabaseCommand> operation)
    {
        ArgumentNullException.ThrowIfNull(operation) /* KLD0041: The BCL null guard accepts this existing reference through its object parameter without copying its contents. */;
        if (operation.Value?.Input /* KLD0041: The existing persisted command slot contains the exact runtime input selected and validated by its closed Kind discriminator. */ is not { } input || !DatabaseCommandInputTypes.TryFind(operation.Value.Kind, out var type)
            || input.GetType() /* KLD0041: This persisted input boundary must verify its exact runtime contract against the closed operation-kind map. */ != type)
        {
            return CommandOutcomePartitionScope.Unknown;
        }
        var target = CommandPartitions.Of(operation.Value.Kind, input) /* KLD0041: The persisted command envelope carries the input selected by Kind; this routing step verifies and reads that native input. */;
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
