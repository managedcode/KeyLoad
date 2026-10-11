namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record CredentialAuthorityState(DiscoveryAuthorityState Document, Guid RepairCommandId)
{
    internal bool? OriginalRepairResult { get; set; }
}
