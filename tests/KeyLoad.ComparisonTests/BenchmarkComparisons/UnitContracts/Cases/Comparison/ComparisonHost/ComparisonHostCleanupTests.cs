using System.Text.Json.Nodes;
using KeyLoad.ComparisonHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;
using KeyLoad.ComparisonTests.Features.BenchmarkComparisons;
using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>An unavailable real native endpoint keeps failed measured accounting and original safe disposal evidence.</summary>
internal sealed class ComparisonHostCleanupTests
{
    private const string TargetProfileField = "targetProfile";

    [Test]
    public async Task NativeSessionEndpointFailureRetainsEveryActualCaseAndDoesNotAcknowledgeAnInitialLoad()
    {
        using var directory = new ImageBundleTestDirectory();
        var settings = SessionHostStartupSupport.ValidSettings();
        settings[SessionHostStartupSupport.Target] = "OpenSearch";
        settings[SessionFields.SessionEnvironment] = "opensearch-n3";
        settings[SessionHostStartupSupport.User] = "native-session-user";
        settings[SessionHostStartupSupport.Password] = SessionHostStartupSupport.Canary;
        settings[SessionFields.OutputEnvironment] = directory.Root;
        settings[SessionHostStartupSupport.Image] = "opensearchproject/opensearch:3.6.0@"
            + global::BenchmarkResources.OpenSearchDigest;
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
            await Assert.That(state[SessionFields.Target]!.GetValue<string>()).IsEqualTo("OpenSearch");
            await Assert.That(state[TargetProfileField] is null).IsTrue();
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

    [Test]
    public async Task RetentionAndBothNativeDisposalFailuresKeepTheirOriginalObjectsInOrder()
    {
        using var directory = new ImageBundleTestDirectory();
        var path = Path.Combine(directory.Root, "original-retention.json");
        await File.WriteAllTextAsync(path, "original");
        var retention = RetainExclusiveAsync(path);
        await using var fixture = await SessionDisposalNativeFixture.CreateAsync(true, true);
        var observed = await NativeComparisonFailure.ObserveAsync(fixture.CompleteAsync(retention).AsTask());
        await Assert.That(observed is AggregateException).IsTrue();
        var failures = ((AggregateException)observed!).InnerExceptions;
        await Assert.That(failures.Count).IsEqualTo(3);
        await Assert.That(ReferenceEquals(failures[0], retention.Exception!.InnerException)).IsTrue();
        await Assert.That(ReferenceEquals(failures[1], fixture.OwnerFailure)).IsTrue();
        await Assert.That(ReferenceEquals(failures[2], fixture.ConfigurationFailure)).IsTrue();
        await Assert.That(fixture.OwnerDisposals).IsEqualTo(1);
        await Assert.That(fixture.ConfigurationDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task SingleNativeDisposalFailureIsRethrownUnwrappedAndConfigurationStillCloses()
    {
        await using var fixture = await SessionDisposalNativeFixture.CreateAsync(true, false);
        var observed = await NativeComparisonFailure.ObserveAsync(fixture.CompleteAsync(Task.CompletedTask).AsTask());
        await Assert.That(ReferenceEquals(observed, fixture.OwnerFailure)).IsTrue();
        await Assert.That(observed is AggregateException).IsFalse();
        await Assert.That(fixture.OwnerDisposals).IsEqualTo(1);
        await Assert.That(fixture.ConfigurationDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task FatalPrimaryKeepsPriorityWhileBothActualNativeLifetimesAreJoined()
    {
        var fatal = new InsufficientMemoryException("controlled-fatal-classification");
        await using var fixture = await SessionDisposalNativeFixture.CreateAsync(false, true);
        var observed = await NativeComparisonFailure.ObserveAsync(fixture.CompleteAsync(Task.FromException(fatal)).AsTask());
        await Assert.That(observed is AggregateException).IsTrue();
        var failures = ((AggregateException)observed!).InnerExceptions;
        await Assert.That(failures.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(failures[0], fatal)).IsTrue();
        await Assert.That(ReferenceEquals(failures[1], fixture.ConfigurationFailure)).IsTrue();
        await Assert.That(fixture.OwnerDisposals).IsEqualTo(1);
        await Assert.That(fixture.ConfigurationDisposals).IsEqualTo(1);
    }

    [Test]
    public async Task FailedOperationAndFailedOriginalReportRetentionKeepBothNativeErrors()
    {
        using var directory = new ImageBundleTestDirectory();
        var missing = Path.Combine(directory.Root, "missing-operation-input.json");
        var operation = File.ReadAllBytesAsync(missing);
        var original = await NativeComparisonFailure.ObserveAsync(operation);
        var report = Path.Combine(directory.Root, "original-failed-report.json");
        await File.WriteAllTextAsync(report, "original");
        var retention = RetainExclusiveAsync(report);
        var observed = await NativeComparisonFailure.ObserveAsync(
            BenchmarkSessionFixtureDisposal.RetainFailureAsync(original!, retention));
        await Assert.That(observed is AggregateException).IsTrue();
        var failures = ((AggregateException)observed!).InnerExceptions;
        await Assert.That(failures.Count).IsEqualTo(2);
        await Assert.That(ReferenceEquals(failures[0], original)).IsTrue();
        await Assert.That(ReferenceEquals(failures[1], retention.Exception!.InnerException)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(report)).IsEqualTo("original");
    }

    private static async Task RetainExclusiveAsync(string path)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(new byte[] { 1 });
    }
}
