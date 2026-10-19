namespace KeyLoad.IntegrationTests.Features.OrleansExecution;

internal sealed record CatalogBootstrapAuthorityState(
    PartitionRef Partition, PrincipalRecord Administrator, string AdministratorKey,
    PrincipalRecord Reader, string ReaderKey, CommandRequest Original,
    CommitReceipt Receipt)
{
    internal Guid ReaderDeniedCommandId { get; } = Guid.NewGuid();
    internal Dictionary<string, string> PhysicalOwners { get; } = new(StringComparer.Ordinal);
    internal CommandRequest? HealthyCommand
    {
        get; set;
    }
    internal CommitReceipt? HealthyReceipt
    {
        get; set;
    }
}
