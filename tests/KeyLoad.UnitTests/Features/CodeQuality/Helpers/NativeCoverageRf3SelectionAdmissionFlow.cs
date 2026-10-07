using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

/// <summary>Actual original source admission after genuine native source preparation.</summary>
internal static class NativeCoverageRf3SelectionAdmissionFlow
{
    private const string ObsoleteFilter = "/*/*/(PartitionQueryPublicRf3Tests)|(McpDocumentCrudParityTests)/*";
    private const string Settings = "scripts/Features/CodeQuality/functional-coverage.production.settings.xml";
    private const string Output = "coverage.coverage";

    internal static async Task VerifyAsync(string path, NativeCoverageRf3Admission original, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(path, token);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [NativeCoverageRf3Protocol.ModeSetting] = NativeCoverageRf3Protocol.Mode,
            [NativeCoverageRf3Protocol.SourceManifestSetting] = path
        }).Build();
        using var lifetime = configuration as IDisposable;
        var options = Options.Create(new NativeCoverageExecutionOptions());
        var failure = Assert.ThrowsExactly<InvalidOperationException>(() => NativeCoverageRf3Selection.Read(
            configuration, TestSuiteProtocol.Rf3Suite, ObsoleteFilter, Settings, Output,
            NativeCoverageProtocol.BinaryFormat, options));
        await Assert.That(failure.Message).IsEqualTo(NativeCoverageRf3Protocol.InvalidSelection);
        var selected = ReadOriginal(configuration, original, options);
        await Assert.That(selected.Admission.SourceManifestSha256).IsEqualTo(original.SourceManifestSha256);
        await Assert.That(selected.Admission.Contributors.SequenceEqual(original.Contributors)).IsTrue();
        await Assert.That(selected.Admission.Server).IsEqualTo(original.Server);
        await Assert.That((await File.ReadAllBytesAsync(path, token)).SequenceEqual(bytes)).IsTrue();
    }
    private static NativeCoverageRf3Selection ReadOriginal(IConfiguration configuration,
        NativeCoverageRf3Admission original, IOptions<NativeCoverageExecutionOptions> options)
    {
        var prior = Environment.GetEnvironmentVariable(NativeCoverageRf3Protocol.GithubShaEnvironment);
        var failures = new List<Exception>();
        NativeCoverageRf3Selection? selected = null;
        try
        {
            ServerFailureObserver.Observe(() =>
            {
                Environment.SetEnvironmentVariable(NativeCoverageRf3Protocol.GithubShaEnvironment, original.SourceRevision);
                selected = NativeCoverageRf3Selection.Read(configuration, TestSuiteProtocol.Rf3Suite,
                    NativeCoverageRf3ContributorCatalog.Filter, Settings, Output, NativeCoverageProtocol.BinaryFormat, options)
                    ?? throw new InvalidOperationException("The genuine original-node selection was not admitted.");
            }, failures);
        }
        finally
        {
            ServerFailureObserver.Observe(() => Environment.SetEnvironmentVariable(
                NativeCoverageRf3Protocol.GithubShaEnvironment, prior), failures);
        }
        global::KeyLoad.UnitTests.Features.CodeQuality.NativeCoverageImageNodeSettlement.ThrowFailures(failures);
        return selected!;
    }

}
