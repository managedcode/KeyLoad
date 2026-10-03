using System.Text.Json;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class Neo4jHarnessRegression
{
    private const string SetupPrefix = "setup:";
    private const string SafeNativePrefix = "Neo4j:";

    public static async Task VerifyAsync(string endpoint, string password, string image, CancellationToken cancellationToken)
    {
        var endpointUri = new Uri(endpoint, UriKind.Absolute);
        await using var fixture = await Neo4jHarnessSchemaFixture.CreateAsync(endpointUri, password, cancellationToken);
        var failures = new List<Exception>();
        await Neo4jHarnessFailureCollector.AttemptAsync(
            () => VerifyCoreAsync(fixture, endpointUri, password, image, cancellationToken), failures);
        await Neo4jHarnessFailureCollector.AttemptAsync(() => fixture.DisposeAsync().AsTask(), failures);
        Neo4jHarnessFailureCollector.ThrowIfAny(Neo4jHarnessConstants.RegressionFailure, failures);
    }

    private static async Task VerifyCoreAsync(Neo4jHarnessSchemaFixture fixture, Uri endpoint, string password, string image,
        CancellationToken cancellationToken)
    {
        using var duplicate = await fixture.ProbeDuplicateConstraintAsync(cancellationToken);
        await Assert.That(duplicate.StatusCode == 202).IsTrue();
        var error = duplicate.Document.RootElement.GetProperty(Neo4jHarnessConstants.ErrorsProperty)[0];
        var nativeCode = error.GetProperty(Neo4jHarnessConstants.ErrorCodeProperty).GetString() ?? string.Empty;
        var nativeMessage = error.GetProperty(Neo4jHarnessConstants.ErrorMessageProperty).GetString() ?? string.Empty;
        await Assert.That(nativeCode.Length > 0).IsTrue();
        await Assert.That(nativeMessage.Length > 0).IsTrue();
        using var successful = JsonDocument.Parse(fixture.ConstraintAcknowledgementJson);
        await Neo4jHarnessProtocolTests.VerifyAsync(successful.RootElement, duplicate.Document.RootElement, nativeCode);
        await VerifyFailedTargetKeepsFixtureAsync(fixture, endpoint, password, image, nativeCode, nativeMessage, cancellationToken);
        await VerifyRunnerSetupFailureAsync(fixture, endpoint, password, image, nativeCode, nativeMessage, cancellationToken);
    }

    private static async Task VerifyFailedTargetKeepsFixtureAsync(Neo4jHarnessSchemaFixture fixture, Uri endpoint,
        string password, string image, string nativeCode, string nativeMessage, CancellationToken cancellationToken)
    {
        using var client = Neo4jHarnessQueryClient.CreateClient(endpoint, password);
        var target = new Neo4jTarget(client, fixture.RunId, image);
        await using (target)
        {
            ComparisonFailureException? failure = null;
            try
            {
                await target.InitializeAsync(SmallDataset(), cancellationToken);
            }
            catch (ComparisonFailureException exception)
            {
                failure = exception;
            }

            await Assert.That(failure is not null).IsTrue();
            await Assert.That(failure!.Message == SafeNativePrefix + nativeCode).IsTrue();
            await Assert.That(!failure.Message.Contains(nativeMessage, StringComparison.Ordinal)).IsTrue();
            await Assert.That(!failure.Message.Contains(password, StringComparison.Ordinal)).IsTrue();
            await Assert.That(!failure.Message.Contains(fixture.RunId, StringComparison.Ordinal)).IsTrue();
        }

        await AssertFixtureRemainsAsync(fixture, cancellationToken);
    }

    private static async Task VerifyRunnerSetupFailureAsync(Neo4jHarnessSchemaFixture fixture, Uri endpoint,
        string password, string image, string nativeCode, string nativeMessage, CancellationToken cancellationToken)
    {
        using var client = Neo4jHarnessQueryClient.CreateClient(endpoint, password);
        var target = new Neo4jTarget(client, fixture.RunId, image);
        await using (target)
        {
            var report = await new ComparisonRunner(SmallOptions()).RunAsync([target], "test", cancellationToken);

            await Assert.That(report.Cases.Length == Enum.GetValues<Scenario>().Length).IsTrue();
            foreach (var item in report.Cases)
            {
                var expectedDetail = SetupPrefix + SafeNativePrefix + nativeCode;
                await Assert.That(item.Status == "failed" && item.Detail == expectedDetail).IsTrue();
                await Assert.That(item.Measurement is null && item.Samples.IsEmpty).IsTrue();
                await Assert.That(!item.Detail!.Contains(nativeMessage, StringComparison.Ordinal)).IsTrue();
                await Assert.That(!item.Detail.Contains(password, StringComparison.Ordinal)).IsTrue();
                await Assert.That(!item.Detail.Contains(fixture.RunId, StringComparison.Ordinal)).IsTrue();
            }
        }

        await AssertFixtureRemainsAsync(fixture, cancellationToken);
    }

    private static async Task AssertFixtureRemainsAsync(Neo4jHarnessSchemaFixture fixture, CancellationToken cancellationToken)
    {
        await Assert.That(await fixture.MarkerExistsAsync(cancellationToken)).IsTrue();
        await Assert.That(await fixture.ConstraintExistsAsync(cancellationToken)).IsTrue();
    }

    private static BenchmarkDataset SmallDataset() => new(SmallOptions());

    private static ComparisonOptions SmallOptions() => new()
    {
        Documents = 16,
        Operations = 12,
        Warmup = 0,
        Repetitions = 1,
        Concurrency = 2,
        PayloadBytes = 128,
        Dimensions = 8,
        TopK = 3,
        GraphVertices = 16,
        GraphFanOut = 2,
        GraphDepth = 2
    };
}
