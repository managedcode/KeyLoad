using System.Text.Json.Nodes;
using KeyLoad.ComparisonTests.Features.BenchmarkComparisons;
using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>An unavailable real native endpoint keeps failed measured accounting and original safe disposal evidence.</summary>
internal sealed class ComparisonHostCleanupTests
{
    [Test]
    public async Task NativeSessionEndpointFailureRetainsEveryActualCaseAndDoesNotAcknowledgeAnInitialLoad()
    {
        using var directory = new ImageBundleTestDirectory();
        var settings = SessionHostStartupSupport.ValidSettings();
        settings[SessionHostStartupSupport.Target] = "Qdrant";
        settings[SessionFields.SessionEnvironment] = "qdrant-n3";
        settings[SessionFields.ApiKeyEnvironment] = SessionHostStartupSupport.Canary;
        settings[SessionFields.OutputEnvironment] = directory.Root;
        settings[SessionHostStartupSupport.Image] = "docker.io/qdrant/qdrant:v1.17.1@"
            + global::BenchmarkResources.QdrantDigest;
        try
        {
            var result = await ComparisonHostProcess.RunAsync([], settings, TestContext.Current!.Execution.CancellationToken);
            var text = result.Stdout + result.Stderr;
            await Assert.That(result.ExitCode).IsNotEqualTo(0);
            await Assert.That(text.Contains(SessionHostStartupSupport.Canary, StringComparison.Ordinal)).IsFalse();
            foreach (var name in SessionHostStartupSupport.NativeNames)
            {
                await Assert.That(text).Contains(name);
            }
            var state = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Root, "session-state.json")))!;
            await Assert.That(state[SessionFields.Outcome]!.GetValue<string>()).IsEqualTo("failure");
            await Assert.That(state[SessionFields.Cases]!.AsArray().Count).IsEqualTo(SessionHostStartupSupport.NativeNames.Length);
            var load = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory.Root, "reports", "load-million.json")))!;
            await Assert.That(load[SessionFields.Status]!.GetValue<string>()).IsEqualTo("failed");
            await Assert.That(load[SessionFields.VerifiedRecords]!.GetValue<int>()).IsEqualTo(0);
            await Assert.That(load[SessionFields.Lanes]!.AsArray().Count).IsEqualTo(0);
            await Assert.That(load[SessionFields.Detail]!.GetValue<string>().Length).IsGreaterThan(0);
        }
        catch (Exception)
        {
            directory.RetainFailureEvidence();
            throw;
        }
    }
}
