using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-METH-001/003: actual native resource bounds admit the canonical document inventory.</summary>
internal sealed class DocumentResourceBudgetTests
{
    private const string KeyLoadTarget = "KeyLoad";
    private const string PostgresTarget = "PostgreSQL + pgvector";
    private const string MaximumConnections = "max_connections=512";
    private const string ConnectionsEvidence = "Benchmarks__Deployment__DocumentPostgresMaximumConnections";
    private const string ScanRecords = "KeyLoad__DatabaseLimits__MaxScanRecords";
    private const string QueryReadBytes = "KeyLoad__DatabaseLimits__MaxQueryReadBytes";
    private const string ScanEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumScanRecords";
    private const string ReadEvidence = "Benchmarks__Deployment__DocumentKeyLoadMaximumQueryReadBytes";
    private const string ExpectedScans = "1100000";
    private const string ExpectedBytes = "2147483648";
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
        await using var fixture = new IsolatedResourceTopologyFixture(PostgresTarget, nodes);
        IsolatedPostgresResources.Add(fixture.Context);
        var resources = fixture.Build();
        foreach (var node in resources)
        {
            var configuration = await IsolatedResourceTopologyFixture.ConfigurationAsync(node);
            await Assert.That(configuration.Arguments.Any(argument => argument.Value == MaximumConnections)).IsTrue();
        }
        var evidence = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Client);
        await Assert.That(evidence[ConnectionsEvidence]).IsEqualTo(ExpectedConnections);
    }

    [Test]
    [Arguments(1)]
    [Arguments(3)]
    public async Task KeyLoadDocumentBoundsCoverMillionPlusCreateAndReadBudgets(int nodes)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(KeyLoadTarget, nodes);
        IsolatedKeyLoadResources.Add(fixture.Context);
        fixture.Build();
        foreach (var node in fixture.Projects)
        {
            var environment = await IsolatedResourceTopologyFixture.EnvironmentAsync(node);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxRequests)]).IsEqualTo(Requests);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxTenantRequests)]).IsEqualTo(Requests);
            await Assert.That(environment[AdmissionPrefix + nameof(HttpAdmissionLimits.MaxPrincipalRequests)]).IsEqualTo(Requests);
            await Assert.That(environment[ScanRecords]).IsEqualTo(ExpectedScans);
            await Assert.That(environment[QueryReadBytes]).IsEqualTo(ExpectedBytes);
        }
        var evidence = await IsolatedResourceTopologyFixture.EnvironmentAsync(fixture.Context.Client);
        await Assert.That(evidence[AdmissionRequests]).IsEqualTo(Requests);
        await Assert.That(evidence[AdmissionMode]).IsEqualTo(bool.TrueString);
        await Assert.That(evidence[ScanEvidence]).IsEqualTo(ExpectedScans);
        await Assert.That(evidence[ReadEvidence]).IsEqualTo(ExpectedBytes);
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
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentPostgresMaximumConnections), "500")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadRequestsPerScope), "500")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumScanRecords), "1000000")]
    [Arguments(nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumQueryReadBytes), "67108864")]
    public async Task InsufficientDocumentBoundsRejectBeforeNativeDirectories(string option, string value)
    {
        await using var fixture = new IsolatedResourceTopologyFixture(KeyLoadTarget, 3);
        fixture.Context.Builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DeploymentPrefix + option] = value
        });
        await Assert.That(() => IsolatedKeyLoadResources.Add(fixture.Context)).Throws<OptionsValidationException>();
        await Assert.That(Directory.Exists(Path.Combine(fixture.Context.Root, NativeDirectory))).IsFalse();
        fixture.Context.Builder.Configuration[DeploymentPrefix + option] = option switch
        {
            nameof(BenchmarkDeploymentOptions.DocumentPostgresMaximumConnections) => ExpectedConnections,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadRequestsPerScope) => Requests,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumScanRecords) => ExpectedScans,
            nameof(BenchmarkDeploymentOptions.DocumentKeyLoadMaximumQueryReadBytes) => ExpectedBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(option))
        };
        IsolatedKeyLoadResources.Add(fixture.Context);
        fixture.Build();
        await Assert.That(fixture.Projects.Length).IsEqualTo(3);
    }
}
