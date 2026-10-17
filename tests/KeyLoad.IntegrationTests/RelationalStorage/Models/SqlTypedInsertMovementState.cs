using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed record SqlTypedInsertMovementState(McpPersistedIdentity Identity, SqlOperationRequest Original,
    CommitReceipt Receipt, SqlOperationRequest Failed, AtomicPartitionPlacementResolution OriginalOwner)
{
    internal AtomicPartitionPlacementResolution? PublishedOwner
    {
        get; init;
    }
    internal SqlOperationRequest? Healthy
    {
        get; init;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get; init;
    }
    internal bool PolicyFenced
    {
        get; init;
    }
}
