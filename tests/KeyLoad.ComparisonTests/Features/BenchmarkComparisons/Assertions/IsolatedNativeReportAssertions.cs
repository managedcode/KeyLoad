using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static partial class IsolatedNativeReportAssertions
{
    private const string CellEnvironment = "KEYLOAD_COMPARISON_CELL_ID";
    private const string JobEnvironment = "KEYLOAD_COMPARISON_JOB_ID";
    private const string WorkerFile = "worker.json";
    private const string ServerResourceFile = "server-resource-evidence.json";
    private const string ServerResourceSchema = "server-resource-evidence.v1";
    private const int MinimumResourceSamples = 2;
    private const string Runner = "comparisons";
    private const string Bootstrap = "bootstrap";
    private const string KeyLoad = "KeyLoad";
    private const string Mongo = "MongoDB";
    private const string UnsupportedTopology = "unsupportedTopology";
    private const string Measured = "measured";
    private const string SolutionFile = "KeyLoad.slnx";

    internal static string EvidenceDirectory()
    {
        var cell = ComparisonImageProtocol.RequiredEnvironment(CellEnvironment);
        if (!CellName().IsMatch(cell))
        {
            throw new InvalidOperationException("The isolated cell ID is invalid.");
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory.Parent is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory.FullName, "artifacts", "comparisons", "isolated", "workers", cell);
    }

    internal static async Task VerifyModelAsync(ContainerResource[] containers, ComparisonWorkerSelection selection,
        CancellationToken cancellationToken)
    {
        var receipt = await ComparisonImageReceipt.ReadAsync(cancellationToken);
        var runner = containers.Single(item => item.Name == Runner);
        await RequireImageAsync(runner, receipt.RunnerImage);
        var unsupported = IsolatedComparisonContract.Current.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        var native = containers.Where(item => item.Name != Runner && !item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        var bootstrap = containers.Where(item => item.Name.Contains(Bootstrap, StringComparison.Ordinal)).ToArray();
        await Assert.That(native.Length).IsEqualTo(unsupported ? 0 : selection.NodeCount);
        await Assert.That(bootstrap.Length).IsEqualTo(selection.Target == Mongo ? 1 : 0);
        foreach (var node in native.Concat(bootstrap))
        {
            await Assert.That(node.TryGetContainerImageName(out var image)).IsTrue();
            await Assert.That(image).Contains(ComparisonImageProtocol.ImageDigestPrefix);
            if (selection.Target == KeyLoad)
            {
                await RequireImageAsync(node, receipt.ServerImage);
            }
        }
    }

    internal static async Task VerifyReportAsync(string output, ComparisonWorkerSelection selection,
        CancellationToken cancellationToken)
    {
        await using var file = File.OpenRead(Path.Combine(output, WorkerFile));
        var envelope = await JsonSerializer.DeserializeAsync<IsolatedComparisonReport>(file, ReportWriter.JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The isolated worker report is missing.");
        await Assert.That(envelope.SchemaVersion).IsEqualTo(4);
        await Assert.That(envelope.Worker.Target).IsEqualTo(selection.Target);
        await Assert.That(envelope.Worker.NodeCount).IsEqualTo(selection.NodeCount);
        await Assert.That(envelope.Worker.Scenario).IsEqualTo(selection.Scenario);
        await Assert.That(envelope.Worker.Profile).IsEqualTo(selection.Profile);
        await Assert.That(envelope.Worker.JobId).IsEqualTo(long.Parse(
            ComparisonImageProtocol.RequiredEnvironment(JobEnvironment), CultureInfo.InvariantCulture));
        await Assert.That(envelope.Worker.SourceRevision).IsEqualTo(
            ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.ShaEnvironment));
        var unsupported = IsolatedComparisonContract.Current.UnsupportedTopologies.SingleOrDefault(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        if (unsupported is not null)
        {
            await Assert.That(envelope.Disposition).IsEqualTo(UnsupportedTopology);
            await Assert.That(envelope.Reason).IsEqualTo(unsupported.Reason);
            await Assert.That(envelope.Report).IsNull();
            return;
        }
        await Assert.That(envelope.Disposition).IsEqualTo(Measured);
        await Assert.That(envelope.Reason).IsNull();
        await RequireMeasurementsAsync(envelope.Report!, selection);
    }

    private static async Task RequireMeasurementsAsync(ComparisonReport report, ComparisonWorkerSelection selection)
    {
        await Assert.That(report.Options).IsEqualTo(selection.Options);
        await Assert.That(report.DatasetSha256).IsEqualTo(new BenchmarkDataset(selection.Options).Sha256);
        var target = report.Targets.Single();
        await Assert.That(target.Name).IsEqualTo(selection.Target);
        await Assert.That(target.Cluster!.Nodes).IsEqualTo(selection.NodeCount);
        await Assert.That(target.Cluster.DataCopies).IsEqualTo(selection.NodeCount);
        await Assert.That(target.Cluster.Observations.IsDefaultOrEmpty).IsFalse();
        await Assert.That(report.Cases.Select(item => item.Repetition).Order().SequenceEqual(
            Enumerable.Range(0, selection.Options.Repetitions))).IsTrue();
        foreach (var item in report.Cases)
        {
            await Assert.That(item.Scenario).IsEqualTo(selection.Scenario);
            await Assert.That(item.Target).IsEqualTo(selection.Target);
            await Assert.That(item.Status is ComparisonStatuses.Measured or ComparisonStatuses.Unsupported).IsTrue();
            if (item.Status == ComparisonStatuses.Measured)
            {
                await Assert.That(item.Measurement!.Attempts).IsEqualTo(selection.Options.Operations);
                await Assert.That(item.Measurement.Successes).IsEqualTo(selection.Options.Operations);
                await Assert.That(item.Measurement.Failures).IsEqualTo(0);
                await Assert.That(item.Samples.Length).IsEqualTo(selection.Options.Operations);
                await Assert.That(item.Samples.All(sample => sample.Success)).IsTrue();
            }
        }
    }

    internal static async Task VerifyServerResourceEvidenceAsync(string output, ComparisonWorkerSelection selection,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(output, ServerResourceFile);
        var info = new FileInfo(path);
        await Assert.That(info.Exists && info.Length <= ScaleServerResourceBounds.MaxSidecarBytes).IsTrue();
        await using var stream = File.OpenRead(path);
        var evidence = await JsonSerializer.DeserializeAsync<ScaleServerResourceEvidence>(stream,
            new JsonSerializerOptions(JsonSerializerDefaults.Web), cancellationToken)
            ?? throw new InvalidDataException("Server resource evidence is malformed.");
        await Assert.That(evidence.Schema).IsEqualTo(ServerResourceSchema);
        await Assert.That(evidence.Target).IsEqualTo(selection.Target);
        await Assert.That(evidence.NodeCount).IsEqualTo(selection.NodeCount);
        await Assert.That(evidence.Scenario).IsEqualTo(selection.Scenario.ToString());
        await Assert.That(evidence.Profile).IsEqualTo(selection.Profile);
        await Assert.That(evidence.SourceRevision).IsEqualTo(ComparisonImageProtocol.RequiredEnvironment(ComparisonImageProtocol.ShaEnvironment));
        await Assert.That(evidence.WorkflowRunId).IsEqualTo(Environment.GetEnvironmentVariable("GITHUB_RUN_ID"));
        await Assert.That(evidence.RunAttempt).IsEqualTo(Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT"));
        await Assert.That(evidence.JobId).IsEqualTo(Environment.GetEnvironmentVariable(JobEnvironment));
        await using var workerStream = File.OpenRead(Path.Combine(output, WorkerFile));
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(workerStream, cancellationToken));
        await Assert.That(evidence.WorkerSha256).IsEqualTo(hash);
        var unsupported = IsolatedComparisonContract.Current.UnsupportedTopologies.Any(item =>
            item.Target == selection.Target && item.NodeCounts.Contains(selection.NodeCount));
        if (unsupported)
        {
            await Assert.That(evidence.Qualified).IsFalse();
            await Assert.That(evidence.Containers).IsEmpty();
            return;
        }
        await Assert.That(evidence.Qualified).IsEqualTo(evidence.MissingEvidence.Length == 0);
        await Assert.That(evidence.Containers.Length <= selection.NodeCount);
        if (evidence.Containers.Length == selection.NodeCount
            && evidence.Containers.All(item => item.SampleCount >= MinimumResourceSamples))
        {
            await Assert.That(evidence.MissingEvidence).DoesNotContain("serverCpuRss");
        }
        if (evidence.Qualified)
        {
            await Assert.That(evidence.Containers.Length).IsEqualTo(selection.NodeCount);
            await Assert.That(evidence.Containers.All(item => item.SampleCount >= MinimumResourceSamples
                && item.ContainerId.Length == 64 && item.ImageId.Length > 0 && item.WritableMounts.Length > 0)).IsTrue();
        }
    }

    internal static void CopyRawIfPresent(string output, string evidence)
    {
        foreach (var name in new[] { WorkerFile, ServerResourceFile })
        {
            var file = Path.Combine(output, name);
            if (File.Exists(file)) File.Copy(file, Path.Combine(evidence, name), overwrite: false);
        }
    }

    private static async Task RequireImageAsync(ContainerResource resource, string expected)
    {
        var digest = expected[(expected.LastIndexOf(ComparisonImageProtocol.ImageDigestPrefix, StringComparison.Ordinal)
            + ComparisonImageProtocol.ImageDigestPrefix.Length)..];
        await Assert.That(resource.Annotations.OfType<ContainerImageAnnotation>().Single().SHA256).IsEqualTo(digest);
    }

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex CellName();
}
