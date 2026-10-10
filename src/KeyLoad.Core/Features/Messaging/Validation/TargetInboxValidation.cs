using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void ValidateInboxPolicy(ResourceDefinition resource)
    {
        if (resource.InboxPolicy is not { } policy)
        { return; }
        if (resource.Kind != ResourceKind.WorkQueue || resource.QueuePolicy is null || policy.MaxReceipts < TargetInboxProtocol.MinimumCount
            || policy.MaxBytes < TargetInboxProtocol.MinimumCount || policy.MaxReceipts > resource.QueuePolicy.MaxStoredMessages
            || policy.MaxBytes > resource.QueuePolicy.MaxStoredBytes)
        { throw Errors.Fail(ErrorCode.Validation, TargetInboxProtocol.Invalid); }
    }

    private static void ValidateInboxInput(CommitInboxRequest request)
    {
        if (request.Target is null || request.Source is null || request.Target.Partition is null || request.Source.Partition is null
            || request.DeliveryGeneration < TargetInboxProtocol.MinimumCount
            || request.ExecutionGeneration < TargetInboxProtocol.MinimumCount || request.Effects.IsDefault)
        { throw Errors.Fail(ErrorCode.Validation, TargetInboxProtocol.Invalid); }
        JsonData.Identifier(request.Target.Queue);
        JsonData.Identifier(request.Source.Queue);
        JsonData.Identifier(request.Source.Partition.TenantId);
        JsonData.Identifier(request.Source.Partition.DatabaseId);
        JsonData.Identifier(request.Source.Partition.TransactionDomainId);
        JsonData.Identifier(request.Source.Partition.PartitionKey);
        JsonData.Identifier(request.MessageId);
        JsonData.Identifier(request.HandlerScope);
    }
}
