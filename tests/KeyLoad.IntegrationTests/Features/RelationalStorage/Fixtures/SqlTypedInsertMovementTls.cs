using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.RelationalStorage;

internal sealed class SqlTypedInsertMovementTls(PartitionRef partition)
{
    internal PartitionRef Partition { get; } = partition;
    private readonly PostgresRf3Selection selection = new(PostgresRf3Protocol.JoinDialect);
    private PostgresRf3Certificate? certificate;
    internal string RootCertificate => certificate?.RootCertificate
        ?? throw new InvalidOperationException(MissingCertificate);
    internal void Configure(IDistributedApplicationTestingBuilder builder, string root)
    {
        certificate ??= PostgresRf3Certificate.Create(root, TwoRf3MembershipProtocol.Nodes);
        selection.Configure(builder, certificate, partition, TwoRf3MembershipProtocol.Nodes);
    }
    private const string MissingCertificate = "The owned movement TLS certificate is absent.";
}

