using System.Globalization;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record PostgresRf3Selection(int QueryDialect, bool ObserveCancellation = false)
{
    private const string Prefix = "KeyLoad__NativeSql__";
    private const string MountedCertificate = "/native-sql/server.pfx";
    private const string MountedDirectory = "/native-sql";
    private const string Tcp = "tcp";
    private const string True = "true";

    internal RequestCqrsProbeFixture? Controls { get; private set; }
    internal bool RetainRoot { get; private set; }

    internal void RetainEvidence()
    { RetainRoot = true; Controls?.RetainEvidence(); }

    internal string[] Prepare(string root, string[] arguments)
    {
        if (!ObserveCancellation)
        { return arguments; }
        Controls = RequestCqrsProbeFixture.Create(root, Guid.NewGuid());
        return [.. arguments, ProbeEnabled, ProbeRoot + Controls.Root, ProbeSession + Controls.SessionId];
    }

    internal Task JoinAfterResourcesAsync(bool resourcesJoined, List<Exception> failures)
    {
        if (Controls is null)
        { return Task.CompletedTask; }
        if (!resourcesJoined || failures.Count != PostgresRf3Protocol.FirstColumn)
        { RetainEvidence(); }
        return global::KeyLoad.Server.ServerFailureObserver.ObserveAsync(Controls.DisposeAfterResourcesJoinedAsync, failures);
    }

    private const string ProbeEnabled = "--KeyLoadTests:RequestCqrsProbe:Enabled=true";
    private const string ProbeRoot = "--KeyLoadTests:RequestCqrsProbe:Root=";
    private const string ProbeSession = "--KeyLoadTests:RequestCqrsProbe:SessionId=";

    internal void Configure(IDistributedApplicationTestingBuilder builder, string root)
    {
        var certificates = PostgresRf3Certificate.Create(root);
        foreach (var number in Enumerable.Range(ClusterFixtureProtocol.FirstNodeNumber, ClusterFixtureProtocol.NodeCount))
        {
            var node = builder.CreateResourceBuilder(builder.Resources.OfType<ContainerResource>()
                .Single(resource => resource.Name == ClusterFixtureProtocol.NodeName(number)));
            node.WithBindMount(certificates.Directory, MountedDirectory, isReadOnly: true)
                .WithEndpoint(targetPort: PostgresRf3Protocol.Port, name: PostgresRf3Protocol.Endpoint,
                    scheme: Tcp, isProxied: false)
                .WithEnvironment(Prefix + Port, PostgresRf3Protocol.Port.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(Prefix + CertificatePath, MountedCertificate)
                .WithEnvironment(Prefix + Dialect, QueryDialect.ToString(CultureInfo.InvariantCulture))
                .WithEnvironment(Prefix + FullScan, True)
                .WithEnvironment(Prefix + DatabasePrefix + Tenant, PostgresRf3Protocol.Partition.TenantId)
                .WithEnvironment(Prefix + DatabasePrefix + Database, PostgresRf3Protocol.Partition.DatabaseId)
                .WithEnvironment(Prefix + DatabasePrefix + Domain, PostgresRf3Protocol.Partition.TransactionDomainId)
                .WithEnvironment(Prefix + DatabasePrefix + Partition, PostgresRf3Protocol.Partition.PartitionKey);
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
