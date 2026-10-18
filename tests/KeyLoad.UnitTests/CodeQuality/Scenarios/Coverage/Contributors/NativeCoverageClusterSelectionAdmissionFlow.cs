using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

/// <summary>Actual original source admission after genuine native source preparation.</summary>
internal static class NativeCoverageClusterSelectionAdmissionFlow
{
    private const string ObsoleteFilter = "/*/*/(PartitionQueryPublicClusterTests)|(McpDocumentCrudParityTests)/*";
    private const string Settings = "scripts/Features/CodeQuality/functional-coverage.production.settings.xml";
    private const string Output = "coverage.coverage";

    internal static async Task VerifyAsync(string path, NativeCoverageClusterAdmission original, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(path, token);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [NativeCoverageClusterProtocol.ModeSetting] = NativeCoverageClusterProtocol.Mode,
            [NativeCoverageClusterProtocol.SourceManifestSetting] = path
        }).Build();
        using var lifetime = configuration as IDisposable;
        var options = Options.Create(new NativeCoverageExecutionOptions());
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => NativeCoverageClusterSelection.Read(
            configuration, TestSuiteProtocol.ClusterSuite, ObsoleteFilter, Settings, Output,
            NativeCoverageProtocol.BinaryFormat, options));
        await Assert.That(failure.Message).IsEqualTo(NativeCoverageClusterProtocol.InvalidSelection);
        var selected = ReadOriginal(configuration, original, options);
        await Assert.That(selected.Admission.SourceManifestSha256).IsEqualTo(original.SourceManifestSha256);
        await Assert.That(selected.Admission.Contributors.SequenceEqual(original.Contributors)).IsTrue();
        await Assert.That(selected.Admission.Server).IsEqualTo(original.Server);
        await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(bytes)).IsTrue();
    }
    private static NativeCoverageClusterSelection ReadOriginal(IConfiguration configuration,
        NativeCoverageClusterAdmission original, IOptions<NativeCoverageExecutionOptions> options)
    {
        var prior = Environment.GetEnvironmentVariable(NativeCoverageClusterProtocol.GithubShaEnvironment);
        var failures = new List<Exception>();
        NativeCoverageClusterSelection? selected = null;
        try
        {
            ServerFailureObserver.Observe(() =>
            {
                Environment.SetEnvironmentVariable(NativeCoverageClusterProtocol.GithubShaEnvironment, original.SourceRevision);
                selected = NativeCoverageClusterSelection.Read(configuration, TestSuiteProtocol.ClusterSuite,
                    NativeCoverageClusterContributorCatalog.Filter, Settings, Output, NativeCoverageProtocol.BinaryFormat, options)
                    ?? throw new InvalidOperationException("The genuine original-node selection was not admitted.");
            }, failures);
        }
        finally
        {
            ServerFailureObserver.Observe(() => Environment.SetEnvironmentVariable(
                NativeCoverageClusterProtocol.GithubShaEnvironment, prior), failures);
        }
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        return selected!;
    }

}
