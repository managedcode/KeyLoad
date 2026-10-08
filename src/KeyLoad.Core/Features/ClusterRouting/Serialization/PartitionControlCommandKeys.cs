using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Serialization;

internal static class PartitionControlCommandKeys
{
    internal static byte[] Original(PartitionControlCommandIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.PrincipalId) || identity.CommandId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var scope = new CommandOutcomePartitionScope(identity.ScopeKind, identity.Partition);
        if (scope.Kind == CommandOutcomeScopeKind.Partition && scope.Partition is not null)
        { DatabaseEngine.ValidatePartition(scope.Partition); }
        return CommandOutcomeKeyResolver.ForNew(identity.PrincipalId, identity.CommandId, scope).Key;
    }

    internal static byte[] Authority(PartitionControlCommandIdentity identity)
        => KeyCodec.Encode(PartitionMoveProtocol.CommandAuthoritySpace, Original(identity));
}
