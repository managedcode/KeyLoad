using System.Text;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>REQ/AC-TEST-007: restart failure receipts retain bounded safe snapshots.</summary>
internal sealed class ContainerRestartFailureCaptureTests
{
    private const int MaximumReceipts = 32;
    private const int AdditionalReceipts = 3;
    private const int MaximumUtf8Bytes = 8_192;
    private const string PrivateCanary = "restart-private-canary";
    private const string ValidResourceId = "node2";
    private const string ClosedUnavailable = "unavailable";
    private const string ClosedOther = "other";
    private const string Emoji = "🙂";

    [Test]
    public async Task AcTest007ProjectionKeepsOnlyValidatedPublicSnapshotFields()
    {
        var (resource, snapshot) = CreatePrivateSnapshot();
        await AssertPrivateSnapshotProjectionAsync(resource, snapshot);
        await AssertGeneratedResourceIdentifiersAsync(resource, snapshot);
        await AssertFailureTextIsExcludedAsync();
    }

    private static (ContainerResource Resource, CustomResourceSnapshot Snapshot) CreatePrivateSnapshot()
    {
        var state = new ResourceStateSnapshot(PrivateCanary, PrivateCanary);
        var snapshot = new CustomResourceSnapshot
        {
            ResourceType = PrivateCanary,
            Properties = [new("property-canary", PrivateCanary)],
            EnvironmentVariables = [new("environment-canary", PrivateCanary, true)],
            Urls = [new("url-canary", "https://" + PrivateCanary, true)],
            State = state,
            ExitCode = 137,
            CreationTimeStamp = new DateTime(2026, 7, 28, 10, 11, 12, DateTimeKind.Utc),
            StartTimeStamp = new DateTime(2026, 7, 28, 10, 11, 13, DateTimeKind.Utc),
            StopTimeStamp = new DateTime(2026, 7, 28, 10, 11, 14, DateTimeKind.Utc)
        };
        var resource = new ContainerResource(ValidResourceId, PrivateCanary);
        return (resource, snapshot);
    }

    private static async Task AssertPrivateSnapshotProjectionAsync(ContainerResource resource,
        CustomResourceSnapshot snapshot)
    {
        var resourceEvent = new ResourceEvent(resource, ValidResourceId, snapshot);
        var line = ContainerRestartDiagnostics.FormatResourceSample(
            ContainerRestartSampleStage.BeforeStart, 1, resourceEvent);

        await Assert.That(line.Contains("BeforeStart", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("resourceId=" + ValidResourceId, StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("type=Container", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains(ClosedUnavailable, StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains(ClosedOther, StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("137", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("creation=2026-07-28T10:11:12.0000000Z", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("start=2026-07-28T10:11:13.0000000Z", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains("stop=2026-07-28T10:11:14.0000000Z", StringComparison.Ordinal)).IsTrue();
        await Assert.That(line.Contains(PrivateCanary, StringComparison.Ordinal)).IsFalse();
        await Assert.That(line.Contains("property-canary", StringComparison.Ordinal)).IsFalse();
        await Assert.That(line.Contains("environment-canary", StringComparison.Ordinal)).IsFalse();
        await Assert.That(line.Contains("url-canary", StringComparison.Ordinal)).IsFalse();

        var healthySnapshot = snapshot with { State = KnownResourceStates.Running };
        var healthyLine = ContainerRestartDiagnostics.FormatResourceSample(
            ContainerRestartSampleStage.AfterStartSucceeded, 2,
            new ResourceEvent(resource, ValidResourceId, healthySnapshot));
        await Assert.That(healthyLine.Contains("state=Running", StringComparison.Ordinal)).IsTrue();
        await Assert.That(healthyLine.Contains("health=Healthy", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertGeneratedResourceIdentifiersAsync(ContainerResource resource,
        CustomResourceSnapshot snapshot)
    {
        var generated = ProjectResourceId(resource, snapshot, "node2-abcdwxyz");
        await Assert.That(generated.Contains("resourceId=node2-abcdwxyz", StringComparison.Ordinal)).IsTrue();

        var nativeHex = ProjectResourceId(resource, snapshot, new string('a', 64));
        await Assert.That(nativeHex.Contains("resourceId=" + new string('a', 64), StringComparison.Ordinal)).IsTrue();

        var privateIdentifier = ProjectResourceId(resource, snapshot, "node2-private-canary");
        await Assert.That(privateIdentifier.Contains("node2-private-canary", StringComparison.Ordinal)).IsFalse();
        await Assert.That(privateIdentifier.Contains("resourceId=unavailable", StringComparison.Ordinal)).IsTrue();
    }

    private static string ProjectResourceId(ContainerResource resource, CustomResourceSnapshot snapshot, string resourceId) =>
        ContainerRestartDiagnostics.FormatResourceSample(ContainerRestartSampleStage.BeforeStart, 3,
            new ResourceEvent(resource, resourceId, snapshot));

    private static async Task AssertFailureTextIsExcludedAsync()
    {
        var failureLine = ContainerRestartDiagnostics.FormatFailure(ContainerRestartStage.HealthWait,
            new InvalidOperationException(PrivateCanary));
        await Assert.That(failureLine.Contains("stage=HealthWait type=InvalidOperation", StringComparison.Ordinal)).IsTrue();
        await Assert.That(failureLine.Contains(PrivateCanary, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    public async Task AcTest007RestartReceiptsKeepFirstAndLatestWithinWholeUtf8Bounds()
    {
        using var scope = new ReceiptDirectory();
        var receipts = new ClusterFailureReceipts(ClusterFailureReceiptKind.Restart);
        for (var sequence = 1; sequence <= MaximumReceipts + AdditionalReceipts; sequence++)
        {
            var resource = new ContainerResource(ValidResourceId, "native-entrypoint");
            var snapshot = new CustomResourceSnapshot
            {
                ResourceType = "container",
                Properties = [],
                State = KnownResourceStates.Exited,
                ExitCode = 137
            };
            var sample = ContainerRestartDiagnostics.FormatResourceSample(
                ContainerRestartSampleStage.Failure, sequence,
                new ResourceEvent(resource, ValidResourceId, snapshot));
            var oversizedTail = string.Concat(Enumerable.Repeat(Emoji, MaximumUtf8Bytes));
            var bounded = BoundedDiagnosticLog.Bound([sample, oversizedTail]);

            receipts.Save(scope.Path, bounded);
        }

        var firstPath = System.IO.Path.Combine(scope.Path, "rf3-restart-failure-0001.log");
        var lastRetainedPath = System.IO.Path.Combine(scope.Path, "rf3-restart-failure-0032.log");
        var latestPath = System.IO.Path.Combine(scope.Path, "rf3-restart-failure.log");
        var files = Directory.GetFiles(scope.Path);
        await Assert.That(files).Count().IsEqualTo(MaximumReceipts + 1);
        await Assert.That(File.Exists(System.IO.Path.Combine(scope.Path, "rf3-restart-failure-0033.log"))).IsFalse();

        var first = await ReadBoundedUtf8Async(firstPath);
        var lastRetained = await ReadBoundedUtf8Async(lastRetainedPath);
        var latest = await ReadBoundedUtf8Async(latestPath);
        await Assert.That(first.Contains("sequence=1", StringComparison.Ordinal)).IsTrue();
        await Assert.That(lastRetained.Contains("sequence=32", StringComparison.Ordinal)).IsTrue();
        await Assert.That(latest.Contains("sequence=35", StringComparison.Ordinal)).IsTrue();
        await Assert.That(first.Contains("Exited", StringComparison.Ordinal)).IsTrue();
        await Assert.That(latest.Contains("Exited", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task<string> ReadBoundedUtf8Async(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(bytes.Length).IsLessThanOrEqualTo(MaximumUtf8Bytes);
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private sealed class ReceiptDirectory : IDisposable
    {
        private const string DirectoryPrefix = "keyload-restart-failure-receipts-";

        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            DirectoryPrefix + Guid.NewGuid().ToString("N"));

        internal ReceiptDirectory() => Directory.CreateDirectory(Path);

        public void Dispose() => Directory.Delete(Path, true);
    }
}
