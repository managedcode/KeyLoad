using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Cleanup-only domains preserve every existing seal and reservation identity byte.</summary>
internal static class ResourceRetirementCleanupAttemptIdentity
{
    private const int GuidBytes = 16;
    private const string CommandSpace = "keyload.resource-retirement.cleanup-attempt-id.v1";
    private const string PreparedSpace = "keyload.resource-retirement.cleanup-prepared-id.v1";

    internal static byte[] Key(string space, ResourceRetirementAttemptContextV1 context, params object?[] tail)
    {
        var control = context.Retirement!;
        var page = context.Page!;
        var owner = context.Receiver!.Owner;
        object?[] scope = [space, control.Request.RetirementId, control.Lifetime.TenantId,
            control.Lifetime.DatabaseId, control.Lifetime.ResourceName, control.Lifetime.Incarnation.Value,
            (int)context.Purpose, control.IntentDigest, control.Catalog.DefaultShard.PhysicalShardId,
            control.Catalog.DefaultShard.Incarnation, owner.PhysicalShardId, owner.Incarnation,
            page.Partition.TenantId, page.Partition.DatabaseId, page.Partition.TransactionDomainId,
            page.Partition.PartitionKey, page.Ordinal];
        return KeyCodec.Encode([.. scope, .. tail]);
    }

    internal static Guid Command(ResourceRetirementAttemptContextV1 context, ResourceRetirementPhase phase, Guid attemptId)
    {
        if (attemptId == Guid.Empty || phase is not (ResourceRetirementPhase.PrepareAttempt or ResourceRetirementPhase.CleanupPage))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }

        return new(SHA256.HashData(Key(CommandSpace, context, (int)phase, attemptId)).AsSpan()[..GuidBytes]);
    }

    internal static Guid Prepared(ResourceRetirementAttemptContextV1 context, Guid attemptId,
        ResourceRetirementPreparedOperationAction action)
    {
        if (attemptId == Guid.Empty || !Enum.IsDefined(action))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }

        return new(SHA256.HashData(Key(PreparedSpace, context, (int)action, attemptId)).AsSpan()[..GuidBytes]);
    }
}
