namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record CredentialAuthorityState(DiscoveryAuthorityState Document, Guid RepairCommandId)
{
    internal global::ManagedCode.Communication.Result? OriginalRepairResult
    {
        get; set;
    }
}
