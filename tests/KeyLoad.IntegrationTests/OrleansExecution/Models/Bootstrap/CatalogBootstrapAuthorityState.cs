using P = KeyLoad.IntegrationTests.OrleansExecution.CatalogBootstrapAuthorityProtocol;

namespace KeyLoad.IntegrationTests.OrleansExecution;

internal sealed record CatalogBootstrapAuthorityState(
    PartitionRef Partition, PrincipalRecord Administrator, string AdministratorKey,
    PrincipalRecord Reader, string ReaderKey, CommandRequest Original,
    CommitReceipt Receipt, EntityRefLifetime Lifetime)
{
    internal EntityRef StoredDocument => new(Partition, P.Collection, P.Document) { Lifetime = Lifetime };
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
