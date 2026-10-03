using System.Text.RegularExpressions;
using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationWorkflowTests
{
    private const string Workflow = "benchmarks.yml";
    private const string NativeJob = "native-serialization";
    private const string NativeOnly = "inputs.native_serialization_only";
    private const string RawOnly = "inputs.raw_storage_only";
    private const string NativeInput = "native_serialization_only";
    private const string RawInputMarker = "      raw_storage_only:";
    private const string TrustedMain = "github.repository == 'managedcode/KeyLoad' && github.ref == 'refs/heads/main'";
    private const string EvidenceCommand = "node scripts/Features/BenchmarkComparisons/native-serialization-evidence.mjs";
    private const string GeneratedHost = "dotnet benchmarks/KeyLoad.Benchmarks/bin/Release/net10.0/KeyLoad.Benchmarks.dll";
    private const string ClosedFilter = "--filter '*Native*SerializationBenchmarks*'";
    private const string TestFilter = "--treenode-filter '/*/*/NativeSerialization*/*'";
    private const string AggregateNeeds = "needs: [comparison-build, comparison-plan, comparison-images, comparison-preflight, comparison-crud, comparison-specialized]";

    [Test]
    public async Task AcIsPerf004DiagnosticInputDefaultsOffAndRejectsConflictingModes()
    {
        var workflow = WorkflowLayoutSource.Read(Workflow);
        var events = WorkflowLayoutSource.EventBlock(workflow);
        var nativeInput = DispatchInput(events, NativeInput);
        await Assert.That(nativeInput.Contains("type: boolean", StringComparison.Ordinal)).IsTrue();
        await Assert.That(nativeInput.Contains("default: false", StringComparison.Ordinal)).IsTrue();
        if (events.Contains(RawInputMarker, StringComparison.Ordinal))
        {
            var build = WorkflowLayoutSource.JobBlock(workflow, "comparison-build");
            await Assert.That(build.Contains("NATIVE_ONLY: \"${{ inputs.native_serialization_only }}\"", StringComparison.Ordinal)).IsTrue();
            await Assert.That(build.Contains("RAW_ONLY: \"${{ inputs.raw_storage_only }}\"", StringComparison.Ordinal)).IsTrue();
            await Assert.That(build.Contains("[ \"$NATIVE_ONLY\" = true ] && [ \"$RAW_ONLY\" = true ]", StringComparison.Ordinal)).IsTrue();
            await Assert.That(build.Contains("exit 1", StringComparison.Ordinal)).IsTrue();
        }
        var prefix = workflow[..workflow.IndexOf("\njobs:", StringComparison.Ordinal)];
        await Assert.That(prefix.Contains("inputs.native_serialization_only && '-native-serialization'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(prefix.Contains("cancel-in-progress: false", StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcIsPerf003OnlyLinuxGeneratedChildrenCanProduceTheDiagnosticArtifact()
    {
        var workflow = WorkflowLayoutSource.Read(Workflow);
        var job = WorkflowLayoutSource.JobBlock(workflow, NativeJob);
        await Assert.That(job.Contains("runs-on: ubuntu-latest", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("needs: comparison-build", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("refs/heads/main", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains(NativeOnly, StringComparison.Ordinal)).IsTrue();
        if (WorkflowLayoutSource.EventBlock(workflow).Contains(RawInputMarker, StringComparison.Ordinal))
        {
            await Assert.That(job.Split('\n').Contains("    if: " + TrustedMain + " && " + NativeOnly + " && !" + RawOnly)).IsTrue();
            foreach (var rawJob in new[] { "raw-storage-correctness", "raw-storage" })
            {
                await Assert.That(WorkflowLayoutSource.JobBlock(workflow, rawJob).Contains("!" + NativeOnly, StringComparison.Ordinal)).IsTrue();
            }
        }
        else
        {
            await Assert.That(job.Split('\n').Contains("    if: " + TrustedMain + " && " + NativeOnly)).IsTrue();
            await Assert.That(job.Contains(RawOnly, StringComparison.Ordinal)).IsFalse();
        }
        await Assert.That(job.Contains(GeneratedHost, StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains(ClosedFilter, StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("--exporters fulljson csv --memory --keepFiles --logBuildOutput --stopOnFirstError", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("--inProcess", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(job.Contains("KEYLOAD_NATIVE_SERIALIZATION_CORPUS_DIRECTORY", StringComparison.Ordinal)).IsTrue();
        await AssertRetainedEvidence(job);
    }

    [Test]
    public async Task AcIsPerf002GeneratedConsumerAndReportContractsRunNormallyAndInScalarMode()
    {
        var job = WorkflowLayoutSource.JobBlock(WorkflowLayoutSource.Read(Workflow), NativeJob);
        var normal = job.IndexOf(TestFilter, StringComparison.Ordinal);
        await Assert.That(normal).IsGreaterThan(-1);
        var scalar = job.IndexOf(TestFilter, normal + TestFilter.Length, StringComparison.Ordinal);
        await Assert.That(scalar > normal).IsTrue();
        await Assert.That(job.Contains("DOTNET_EnableHWIntrinsic: 0", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("artifacts/native-diagnostic-tests/normal", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("artifacts/native-diagnostic-tests/scalar", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.IndexOf("--mode=prepare", StringComparison.Ordinal) > scalar).IsTrue();
    }

    [Test]
    public async Task AcIsPerf004DefaultCompleteMatrixAndWebsiteGatesRemainIndependent()
    {
        var workflow = WorkflowLayoutSource.Read(Workflow);
        foreach (var jobId in new[] { "comparison-plan", "comparison-images" })
        {
            var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
            await Assert.That(job.Contains("!" + NativeOnly, StringComparison.Ordinal)).IsTrue();
        }
        var aggregate = WorkflowLayoutSource.JobBlock(workflow, "comparison-aggregate");
        await Assert.That(aggregate.Contains(AggregateNeeds, StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("\n    if:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(aggregate.Contains(NativeJob, StringComparison.Ordinal)).IsFalse();
        var qualify = WorkflowLayoutSource.JobBlock(workflow, "qualify");
        await Assert.That(qualify.Contains("needs: comparison-aggregate", StringComparison.Ordinal)).IsTrue();
        await Assert.That(qualify.Contains("\n    if:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(WorkflowLayoutSource.JobBlock(workflow, "deploy").Contains("needs: qualify", StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(workflow, NativeJob).Contains("pages: write", StringComparison.Ordinal)).IsFalse();
    }

    private static string DispatchInput(string events, string name)
    {
        var marker = "\n      " + name + ":\n";
        var start = events.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("The native diagnostic dispatch input is absent.");
        }
        var end = new Regex("^ {0,6}\\S", RegexOptions.Multiline, TimeSpan.FromSeconds(1)).Match(events, start + marker.Length);
        return end.Success ? events[start..end.Index] : events[start..];
    }

    private static async Task AssertRetainedEvidence(string job)
    {
        await Assert.That(job.Contains(EvidenceCommand + " --mode=prepare", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains(EvidenceCommand + " --mode=verify", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("stdout.txt", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("stderr.txt", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("exit \"$status\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("if: always()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("name: native-serialization-measurements", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("if-no-files-found: error", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("retention-days: 90", StringComparison.Ordinal)).IsTrue();
    }
}
