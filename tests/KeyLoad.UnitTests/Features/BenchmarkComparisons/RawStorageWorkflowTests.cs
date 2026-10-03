using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class RawStorageWorkflowTests
{
    private const string BenchmarksWorkflowFile = "benchmarks.yml";
    private const string RawActionPath = ".github/workflows/Features/BenchmarkComparisons/RawStorageEvaluation/action.yml";
    private const string RawValidatorPath = "scripts/Features/BenchmarkComparisons/raw-storage-evidence.mjs";
    private const string WorkflowDispatch = "workflow_dispatch:";
    private const string RawStorageOnly = "raw_storage_only";
    private const string DisabledByDefault = "default: false";
    private const string JobCondition = "if:";
    private const string KeepIndependentCells = "fail-fast: false";
    private const string ExcludeRawOnly = "!inputs.raw_storage_only";
    private const string JobsBoundary = "\njobs:";
    private const string ConcurrencyExpression = "group: keyload-benchmarks-${{ github.ref }}${{ inputs.raw_storage_only && '-raw-storage' || '' }}${{ inputs.native_serialization_only && '-native-serialization' || '' }}";
    private const string PreserveRunningWorkflow = "cancel-in-progress: false";
    private const string PrepareMode = "--mode=prepare";
    private const string VerifyMode = "--mode=verify";
    private const string RetainFailure = "if: always()";
    private const string RequireArtifact = "if-no-files-found: error";
    private const string BuildJob = "comparison-build";
    private const string PlanJob = "comparison-plan";
    private const string ImagesJob = "comparison-images";
    private const string CorrectnessJob = "raw-storage-correctness";
    private const string RawJob = "raw-storage";
    private const string ActionReference = "./.github/workflows/Features/BenchmarkComparisons/RawStorageEvaluation";
    private const string CorrectnessMode = "correctness";
    private const string BenchmarkMode = "benchmark";
    private const string RawEngineVariable = "KEYLOAD_RAW_STORAGE_ENGINE";
    private const string EngineMatrix = "engine: [zonetree, tsavorite]";
    private const string UbuntuRunner = "runs-on: ubuntu-latest";
    private const string ComparisonBuildDependency = "needs: comparison-build";
    private const string BenchmarkDependencies = "needs: [comparison-build, raw-storage-correctness]";
    private const string RawModeCondition = "inputs.raw_storage_only";
    private const string RawValidatorInvocation = "node scripts/Features/BenchmarkComparisons/raw-storage-evidence.mjs";
    private const string DotnetBuild = "dotnet build KeyLoad.slnx --no-restore --configuration Release";
    private const string FullJsonExporter = "fulljson";
    private const string CsvExporter = "csv";
    private const string StdoutArtifact = "stdout";
    private const string RuntimeIdentity = "RuntimeVersion";
    private const string SourceIdentity = "GITHUB_SHA";
    private const string NormalUnitCommand = "dotnet test --project tests/KeyLoad.UnitTests";
    private const string ScalarIntrinsicSetting = "DOTNET_EnableHWIntrinsic";
    private const string GarnetPackage = "Microsoft.Garnet";
    private const string ZoneTreePackage = "ZoneTree";
    private const string BenchmarkDotNetPackage = "BenchmarkDotNet";

    [Test]
    public async Task AcGe005OptInPreservesDefaultServiceWorkflowAndIsolatesConcurrency()
    {
        var workflow = WorkflowLayoutSource.Read(BenchmarksWorkflowFile);
        var events = WorkflowLayoutSource.EventBlock(workflow);
        await Assert.That(events.Contains(WorkflowDispatch, StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains(RawStorageOnly, StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains(DisabledByDefault, StringComparison.Ordinal)).IsTrue();
        var concurrency = workflow[..workflow.IndexOf(JobsBoundary, StringComparison.Ordinal)];
        await Assert.That(concurrency.Contains(ConcurrencyExpression, StringComparison.Ordinal)).IsTrue();
        await Assert.That(concurrency.Contains(PreserveRunningWorkflow, StringComparison.Ordinal)).IsTrue();
        await AssertThatServiceJobIsRawOnlyGuarded(workflow, PlanJob);
        await AssertThatServiceJobIsRawOnlyGuarded(workflow, ImagesJob);
        await Assert.That(WorkflowLayoutSource.JobIds(workflow).Contains(BuildJob, StringComparer.Ordinal)).IsTrue();
        var build = WorkflowLayoutSource.JobBlock(workflow, BuildJob);
        await Assert.That(build.Contains(DotnetBuild, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcGe005CorrectnessAndEngineMatrixAreSeparateLinuxJobs()
    {
        var workflow = WorkflowLayoutSource.Read(BenchmarksWorkflowFile);
        var jobs = WorkflowLayoutSource.JobIds(workflow);
        await Assert.That(jobs.Contains(CorrectnessJob, StringComparer.Ordinal)).IsTrue();
        await Assert.That(jobs.Contains(RawJob, StringComparer.Ordinal)).IsTrue();

        var correctness = WorkflowLayoutSource.JobBlock(workflow, CorrectnessJob);
        await Assert.That(correctness.Contains(ComparisonBuildDependency, StringComparison.Ordinal)).IsTrue();
        await Assert.That(correctness.Contains(JobCondition, StringComparison.Ordinal)
            && correctness.Contains(RawModeCondition, StringComparison.Ordinal)).IsTrue();
        await Assert.That(correctness.Contains(ActionReference, StringComparison.Ordinal)).IsTrue();
        await Assert.That(correctness.Contains(CorrectnessMode, StringComparison.Ordinal)).IsTrue();
        await Assert.That(correctness.Contains(UbuntuRunner, StringComparison.Ordinal)).IsTrue();

        var benchmark = WorkflowLayoutSource.JobBlock(workflow, RawJob);
        await Assert.That(benchmark.Contains(BenchmarkDependencies, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(JobCondition, StringComparison.Ordinal)
            && benchmark.Contains(RawModeCondition, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(ActionReference, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(BenchmarkMode, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(EngineMatrix, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(RawEngineVariable, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(UbuntuRunner, StringComparison.Ordinal)).IsTrue();
        await Assert.That(benchmark.Contains(KeepIndependentCells, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AcGe005CompositeActionRetainsActualRunnerAndEvidenceValidator()
    {
        var root = IsolatedAggregateNodeProcess.RepositoryRoot();
        var actionPath = Path.Combine(root, RawActionPath);
        var validatorPath = Path.Combine(root, RawValidatorPath);
        await Assert.That(File.Exists(actionPath)).IsTrue();
        await Assert.That(File.Exists(validatorPath)).IsTrue();

        var action = await File.ReadAllTextAsync(actionPath, TestContext.Current!.Execution.CancellationToken);
        var evidence = await File.ReadAllTextAsync(validatorPath, TestContext.Current!.Execution.CancellationToken);
        await Assert.That(action.Contains(CorrectnessMode, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(BenchmarkMode, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(RawValidatorInvocation, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(PrepareMode, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(VerifyMode, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(RetainFailure, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(RequireArtifact, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(NormalUnitCommand, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(ScalarIntrinsicSetting, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(FullJsonExporter, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(CsvExporter, StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(StdoutArtifact, StringComparison.OrdinalIgnoreCase)).IsTrue();
        await Assert.That(evidence.Contains(RuntimeIdentity, StringComparison.Ordinal)).IsTrue();
        await Assert.That(evidence.Contains(SourceIdentity, StringComparison.Ordinal)).IsTrue();
        await Assert.That(evidence.Contains(GarnetPackage, StringComparison.Ordinal)).IsTrue();
        await Assert.That(evidence.Contains(ZoneTreePackage, StringComparison.Ordinal)).IsTrue();
        await Assert.That(evidence.Contains(BenchmarkDotNetPackage, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertThatServiceJobIsRawOnlyGuarded(string workflow, string jobId)
    {
        var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
        await Assert.That(job.Contains(JobCondition, StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains(RawModeCondition, StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains(ExcludeRawOnly, StringComparison.Ordinal)).IsTrue();
    }
}
