using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedDocumentResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedDocumentResourcePostgresTests
{
    private const string Target = "PostgreSQL + pgvector";
    private const string Prefix = "isolated-postgres-";
    private const string Password = "POSTGRES_PASSWORD";
    private const string StandbyPassword = "PGPASSWORD";
    private const string ApplicationName = "PGAPPNAME";
    private const string Data = "/var/lib/postgresql";
    private const string EntryScript = "/bootstrap/isolated-postgres.sh";
    private const string InitScript = "/docker-entrypoint-initdb.d/isolated-replication.sh";
    private const string StandbyCount = "KEYLOAD_POSTGRES_STANDBYS";
    private const string SlotRetention = "max_slot_wal_keep_size=512MB";

    /// <summary>AC-ISO-002/003/006: authentic PG18 bootstrap, private native copies and secret-only authentication.</summary>
    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task NativeGroupHasExactPrimaryAndNamedPhysicalStandbys(int count)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(Target, count);
        IsolatedPostgresResources.Add(fixture.Context);
        var resources = fixture.Build();
        var nodes = resources.Where(node => node.Name != IsolatedResourceTopologyFixture.RunnerName).ToArray();
        await IsolatedDocumentResourceAssertions.VerifyNodesAsync(nodes, count, Prefix, BenchmarkResources.PostgresDigest);
        await IsolatedResourceTopologyFixture.VerifyWaitsAsync(fixture.Context.Runner.Resource, nodes);
        await IsolatedDocumentResourceAssertions.VerifyEndpointsAsync(fixture.Context.Runner.Resource, nodes, T.Tcp);
        var primary = nodes[0];
        var password = await IsolatedDocumentResourceAssertions.SecretAsync(primary, Password);
        var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(environment[IsolatedDocumentResourceAssertions.Image]).IsEqualTo(
            "docker.io/pgvector/pgvector:0.8.6-pg18@" + BenchmarkResources.PostgresDigest);
        await Assert.That(environment.ContainsKey(IsolatedDocumentResourceAssertions.Connection)).IsTrue();
        foreach (var node in nodes)
        {
            await IsolatedDocumentResourceAssertions.VerifyDataAsync(node, Data, fixture.Context.Root);
            await IsolatedDocumentResourceAssertions.VerifyScriptMountAsync(node, EntryScript);
            await Assert.That(node.Entrypoint).IsEqualTo(T.CommandShell);
            var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
            await Assert.That(configuration.Arguments.Select(argument => argument.Value).SequenceEqual(
                new[] { EntryScript, T.Postgres, T.PostgresConfiguration, T.Fsync, T.PostgresConfiguration, T.Commit,
                    T.PostgresConfiguration, SlotRetention }, StringComparer.Ordinal)).IsTrue();
            await Assert.That(configuration.EnvironmentVariables.ToDictionary()[T.PgDataEnvironment]).IsEqualTo(T.PgData);
        }
        await IsolatedDocumentResourceAssertions.VerifyScriptMountAsync(primary, InitScript);
        var primaryEnvironment = await IsolatedResourceTopologyFixture.EnvironmentAsync(primary);
        await Assert.That(primaryEnvironment[StandbyCount]).IsEqualTo(
            (count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 1; index < count; index++)
        {
            var node = nodes[index];
            await Assert.That(await IsolatedDocumentResourceAssertions.SecretAsync(node, StandbyPassword)).IsSameReferenceAs(password);
            var settings = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(settings[ApplicationName]).IsEqualTo("benchmark_standby" + index);
            await Assert.That(settings.ContainsKey(StandbyCount)).IsFalse();
            await Assert.That(node.Annotations.OfType<WaitAnnotation>().Single().Resource).IsSameReferenceAs(primary);
        }
    }
}
