using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-001/003: actual native resource bounds admit the canonical document inventory.</summary>
internal sealed class DocumentResourceBudgetTests
{
    private const string KeyLoadTarget = "KeyLoad";
    private const string PostgresTarget = "PostgreSQL + pgvector";
    private const int MillionRecords = 1_000_000;
    private const int IngestionClients = 500;
    private const string MaximumConnections = "max_connections=512";
    private const string ConnectionsEvidence = "Benchmarks__Deployment__DocumentPostgresMaximumConnections";
    private const string ScanRecords = "KeyLoad__DatabaseLimits__MaxScanRecords";
    private const string QueryReadBytes = "KeyLoad__DatabaseLimits__MaxQueryReadBytes";
    private const string SnapshotThreshold = "KeyLoad__SnapshotThreshold";
    private const string ScanEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumScanRecords";
    private const string ReadEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumQueryReadBytes";
    private const string SnapshotEvidence = "Benchmarks__Deployment__DocumentKeyLoadSnapshotThreshold";
    private const string ExpectedScans = "1100000";
    private const string ExpectedBytes = "2147483648";
    private const string ExpectedSnapshot = "2000000";
    private const string ExpectedConnections = "512";
    private const string DeploymentPrefix = "Benchmarks:Deployment:";
    private const string AdmissionPrefix = "KeyLoad__HttpAdmission__";
    private const string Requests = "512";
    private const string AdmissionRequests = "IsolatedKeyLoadAdmission__RequestsPerScope";
    private const string AdmissionMode = "IsolatedKeyLoadAdmission__DocumentWorkload";
    private const string NativeDirectory = "native";

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task PostgreSqlDocumentClientsHaveNativeConnectionHeadroom(int nodes)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(PostgresTarget, nodes,
            documents: new(DocumentComparisonScenario.Ingest, MillionRecords, IngestionClients));
        IsolatedPostgresResources.Add(fixture.Context);
        var resources = fixture.Build();
        foreach (var node in resources.Where(resource => resource != fixture.Context.Runner.Resource))
        {
            var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
            await Assert.That(configuration.Arguments.Any(argument => argument.Value == MaximumConnections)).IsTrue();
        }
        var evidence = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(evidence[ConnectionsEvidence]).IsEqualTo(ExpectedConnections);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task KeyLoadDocumentBoundsCoverMillionPlusCreateAndRetainSnapshotPolicy(int nodes)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(KeyLoadTarget, nodes,
            documents: new(DocumentComparisonScenario.Create, MillionRecords, DocumentComparisonContract.Current.Clients));
        IsolatedKeyLoadResources.Add(fixture.Context);
        var resources = fixture.Build();
        foreach (var node in resources.Where(resource => resource != fixture.Context.Runner.Resource))
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxRequests)]).IsEqualTo(Requests);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxTenantRequests)]).IsEqualTo(Requests);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxPrincipalRequests)]).IsEqualTo(Requests);
            await Assert.That(environment[ScanRecords]).IsEqualTo(ExpectedScans);
            await Assert.That(environment[QueryReadBytes]).IsEqualTo(ExpectedBytes);
            await Assert.That(environment[SnapshotThreshold]).IsEqualTo(ExpectedSnapshot);
        }
        var evidence = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Runner.Resource);
        await Assert.That(evidence[AdmissionRequests]).IsEqualTo(Requests);
        await Assert.That(evidence[AdmissionMode]).IsEqualTo(bool.TrueString);
        await Assert.That(evidence[ScanEvidence]).IsEqualTo(ExpectedScans);
        await Assert.That(evidence[ReadEvidence]).IsEqualTo(ExpectedBytes);
        await Assert.That(evidence[SnapshotEvidence]).IsEqualTo(ExpectedSnapshot);
    }

    [Test]
    [Arguments(false, 512)]
    [Arguments(true, 32)]
    [Arguments(true, 500)]
    [Arguments(true, 513)]
    public void DocumentAdmissionRequiresExactDeclaredMode(bool document, int requests)
    {
        var options = new IsolatedKeyLoadAdmissionOptions { DocumentWorkload = document, RequestsPerScope = requests };
        Assert.ThrowsExactly<OptionsValidationException>(options.Validate);
    }

    [Test]
    public async Task OrdinaryKeyLoadControlDoesNotReceiveDocumentBudgets()
    {
        await using var fixture = new IsolatedResourceTopologyFixture(KeyLoadTarget, 3);
        IsolatedKeyLoadResources.Add(fixture.Context);
        foreach (var node in fixture.Build().Where(resource => resource != fixture.Context.Runner.Resource))
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxRequests)]).IsEqualTo("32");
            await Assert.That(environment.ContainsKey(ScanRecords)).IsFalse();
            await Assert.That(environment.ContainsKey(QueryReadBytes)).IsFalse();
            await Assert.That(environment[SnapshotThreshold]).IsNotEqualTo(ExpectedSnapshot);
        }
    }

    [Test]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentPostgresMaximumConnections), "500")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadRequestsPerScope), "500")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumScanRecords), "1000000")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumQueryReadBytes), "67108864")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadSnapshotThreshold), "1024")]
    public async Task InsufficientDocumentBoundsRejectBeforeNativeDirectories(string option, string value)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(KeyLoadTarget, 3,
            documents: new(DocumentComparisonScenario.Ingest, MillionRecords, IngestionClients));
        fixture.Context.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        { [DeploymentPrefix + option] = value });
        await Assert.That(() => IsolatedKeyLoadResources.Add(fixture.Context)).Throws<OptionsValidationException>();
        await Assert.That(Directory.Exists(Path.Combine(fixture.Context.Root, NativeDirectory))).IsFalse();
        fixture.Context.Builder.Configuration[DeploymentPrefix + option] = option switch
        {
            nameof(BenchmarkDeploymentOptions.DocumentPostgresMaximumConnections) => ExpectedConnections,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadRequestsPerScope) => Requests,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumScanRecords) => ExpectedScans,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumQueryReadBytes) => ExpectedBytes,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadSnapshotThreshold) => ExpectedSnapshot,
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
        IsolatedKeyLoadResources.Add(fixture.Context);
        var resources = fixture.Build();
        await Assert.That(resources.Where(resource => resource != fixture.Context.Runner.Resource).Count()).IsEqualTo(3);
    }
}
