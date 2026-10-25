using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.ClusterTests.StorageReplication;

namespace KeyLoad.ClusterTests.QueryExecution;

internal sealed record PostgresClusterSelection(int QueryDialect)
{
    private const string Prefix = "KeyLoad__RuntimeSql__";
    private const string Tcp = "tcp";
    private const string True = "true";

    internal void Configure(IDistributedApplicationTestingBuilder builder, string root)
    {
        Configure(builder, PostgresClusterCertificate.Create(root), PostgresClusterProtocol.Partition, Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount)
                .Select(ClusterFixtureProtocol.NodeName));
    }
    internal void Configure(IDistributedApplicationTestingBuilder builder, PostgresClusterCertificate certificates,
        PartitionRef partition, IEnumerable<string> nodes)
    {
        foreach (var name in nodes)
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ProjectResource>()
                .Single(resource => resource.Name == name));
            node.WithEndpoint(name: PostgresClusterProtocol.Endpoint,
                    scheme: Tcp, isProxied: false)
                .WithEnvironment(Prefix + Port, node.GetEndpoint(PostgresClusterProtocol.Endpoint).Property(EndpointProperty.TargetPort))
                .WithEnvironment(Prefix + CertificatePath, certificates.ServerPath)
                .WithEnvironment(Prefix + Dialect, QueryDialect.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(Prefix + FullScan, True)
                .WithEnvironment(Prefix + DatabasePrefix + Tenant, partition.TenantId)
                .WithEnvironment(Prefix + DatabasePrefix + Database, partition.DatabaseId)
                .WithEnvironment(Prefix + DatabasePrefix + Domain, partition.TransactionDomainId)
                .WithEnvironment(Prefix + DatabasePrefix + Partition, partition.PartitionKey);
        }
    }

    private const string Port = "Port";
    private const string CertificatePath = "CertificatePath";
    private const string Dialect = "QueryDialectVersion";
    private const string FullScan = "AllowFullScan";
    private const string DatabasePrefix = "Databases__keyload__";
    private const string Tenant = "TenantId";
    private const string Database = "DatabaseId";
    private const string Domain = "TransactionDomainId";
    private const string Partition = "PartitionKey";
}

