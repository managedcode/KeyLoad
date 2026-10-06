using System.Globalization;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteResources
{
    private const string GithubRevisionEnvironment = "GITHUB_SHA";
    private const string GithubActionsEnvironment = "GITHUB_ACTIONS";
    private const string BenchmarkTargetEnvironment = "Benchmarks__Target";
    private const string BenchmarkScaleEnvironment = "Benchmarks__ScaleProfile";
    private const string BenchmarkVectorEnvironment = "Benchmarks__VectorProfile";
    private const string IntrinsicsEnvironment = "DOTNET_EnableHWIntrinsic";
    private const string NoBuildArgument = "--no-build";
    private const string NoRestoreArgument = "--no-restore";
    private const string ConfigurationArgument = "--configuration";
    private const string ReleaseConfiguration = "Release";
    private const string ResultsDirectoryArgument = "--results-directory";
    private const string CoverageSettingsArgument = "--coverage-settings";
    private const string CoverageFormatArgument = "--coverage-output-format";
    private const string CoverageOutputArgument = "--coverage-output";
    private const string TestFilterArgument = "--treenode-filter";
    private const string MaximumParallelTestsArgument = "--maximum-parallel-tests";

    internal static void Add(IDistributedApplicationBuilder builder, TestSuiteSettings settings)
    {
        const string SourceRootRelativePath = "../..";
        const string SolutionFileName = "KeyLoad.slnx";
        const string SourceCheckoutRequired = "The test AppHost must run from the KeyLoad source checkout.";
        const string DefaultResultsDirectoryName = "TestResults";
        const string DisabledIntrinsicsValue = "0";

        var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, SourceRootRelativePath));
        if (!File.Exists(Path.Combine(root, SolutionFileName)))
        {
            throw new InvalidOperationException(SourceCheckoutRequired);
        }
        var resultsDirectory = ResolvePath(root, settings.ResultsDirectory ?? Path.Combine(DefaultResultsDirectoryName, settings.Suite));
        var arguments = BuildArguments(root, settings, resultsDirectory);
        var runner = CreateRunner(builder, settings.ResourceName, root, arguments);
        if (settings.NativeCoverageRf3 is not null)
        {
            ConfigureNativeCoverage(builder, runner, settings, root, resultsDirectory);
        }
        if (settings.LocalRf3ImageEnabled)
        {
            ConfigureLocalImage(builder, runner, root);
        }
        if (settings.Suite == TestSuiteProtocol.ComparisonSuite && settings.ComparisonTarget is not null)
        {
            runner.WithEnvironment(BenchmarkTargetEnvironment, settings.ComparisonTarget);
        }
        if (settings.ScaleProfile is not null)
        {
            runner.WithEnvironment(BenchmarkScaleEnvironment, settings.ScaleProfile.Id);
        }
        if (settings.VectorProfile is not null)
        {
            runner.WithEnvironment(BenchmarkVectorEnvironment, settings.VectorProfile.Id);
        }
        if (settings.OpenLoopRate is { } rate)
        {
            runner.WithEnvironment(TestSuiteSelectionValidator.OpenLoopNativeRateEnvironment,
                rate.ToString(CultureInfo.InvariantCulture));
        }
        if (settings.Suite == TestSuiteProtocol.ScalarUnitSuite)
        {
            runner.WithEnvironment(IntrinsicsEnvironment, DisabledIntrinsicsValue);
        }
    }

    private static IResourceBuilder<ExecutableResource> CreateRunner(IDistributedApplicationBuilder builder,
        string name, string root, string[] arguments)
    {
        const string Command = "dotnet";
        return builder.AddExecutable(name, Command, root, arguments)
            .WithEnvironment(TestSuiteSettings.SuiteEnvironment, string.Empty)
            .WithEnvironment(TestSuiteSettings.ScaleProfileEnvironment, string.Empty)
            .WithEnvironment(TestSuiteSettings.VectorProfileEnvironment, string.Empty)
            .WithEnvironment(TestSuiteSelectionValidator.OpenLoopRateEnvironment, string.Empty);
    }

    private static void ConfigureLocalImage(IDistributedApplicationBuilder builder,
        IResourceBuilder<ExecutableResource> runner, string root)
    {
        const string LocalImageChildEnvironment = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
        const string EnabledValue = "true";
        const string ImageReceiptEnvironment = "KEYLOAD_IMAGE_RECEIPT";
        var execution = LocalRf3ImageExecution.Create(root);
        var preparation = LocalRf3ImagePrerequisite.Add(builder, root, execution);
        runner.WaitForCompletion(preparation);
        runner.WithEnvironment(LocalRf3ImageExecution.ProvenanceEnvironment, LocalRf3ImageExecution.Provenance)
            .WithEnvironment(LocalRf3ImageExecution.ImageReferenceEnvironment, execution.ImageReference)
            .WithEnvironment(LocalRf3ImageExecution.ReceiptEnvironment, execution.ReceiptPath)
            .WithEnvironment(LocalImageChildEnvironment, EnabledValue)
            .WithEnvironment(LocalRf3ImageRequest.EnabledEnvironment, string.Empty)
            .WithEnvironment(ImageReceiptEnvironment, string.Empty)
            .WithEnvironment(GithubRevisionEnvironment, string.Empty)
            .WithEnvironment(GithubActionsEnvironment, string.Empty);
    }

    private static void ConfigureNativeCoverage(IDistributedApplicationBuilder builder,
        IResourceBuilder<ExecutableResource> runner, TestSuiteSettings settings, string root,
        string resultsDirectory)
    {
        const long TicksPerMillisecond = TimeSpan.TicksPerMillisecond;
        const long NoElapsedTicks = 0;
        var selected = settings.NativeCoverageRf3
            ?? throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        var runtime = AppHostOptionsRegistration.Get(builder);
        var coverage = runtime.NativeCoverage.Value;
        var execution = runtime.TestExecution.Value;
        var pollTicks = execution.ProcessExitPollInterval.Ticks;
        if (pollTicks <= NoElapsedTicks || pollTicks % TicksPerMillisecond != NoElapsedTicks
            || execution.ProcessExitPollInterval > coverage.ShutdownTimeout)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        var preparation = NativeCoverageRf3Prerequisite.Add(builder, settings, root, resultsDirectory,
            out var run);
        runner.WaitForCompletion(preparation);
        NativeCoverageRf3ExecutionEnvironment.Apply(runner, runtime.NativeCoverage,
            selected.Admission.SourceManifestPath);
        runner.WithEnvironment(NativeCoverageRf3Protocol.ServerModeEnvironment, NativeCoverageRf3Protocol.Mode)
            .WithEnvironment(NativeCoverageRf3Protocol.SourceManifestEnvironment,
                selected.Admission.SourceManifestPath)
            .WithEnvironment(NativeCoverageRf3Protocol.RunIdEnvironment, run.RunId)
            .WithEnvironment(NativeCoverageRf3Protocol.RunManifestEnvironment, run.ManifestPath)
            .WithEnvironment(NativeCoverageRf3Protocol.ImageReferenceEnvironment, run.ImageReference)
            .WithEnvironment(NativeCoverageRf3Protocol.StartupPollEnvironment,
                (pollTicks / TicksPerMillisecond).ToString(CultureInfo.InvariantCulture));
    }

    private static string[] BuildArguments(string root, TestSuiteSettings settings, string resultsDirectory)
    {
        const string TestCommand = "test";
        const string ProjectArgument = "--project";
        const string TestsDirectoryName = "tests";
        const string TrxReportArgument = "--report-trx";
        const string EnableCoverageArgument = "--coverage";

        var arguments = new List<string>
        {
            TestCommand, ProjectArgument, Path.Combine(root, TestsDirectoryName, settings.Project),
            NoBuildArgument, NoRestoreArgument, ConfigurationArgument, ReleaseConfiguration,
            ResultsDirectoryArgument, resultsDirectory
        };
        arguments.Add(MaximumParallelTestsArgument);
        arguments.Add(settings.MaximumParallelTests.ToString(CultureInfo.InvariantCulture));
        if (settings.ReportTrx)
        {
            arguments.Add(TrxReportArgument);
        }
        if (settings.CoverageSettings is not null && settings.CoverageOutput is not null)
        {
            arguments.Add(EnableCoverageArgument);
            arguments.Add(CoverageSettingsArgument);
            arguments.Add(ResolvePath(root, settings.CoverageSettings));
            arguments.Add(CoverageFormatArgument);
            arguments.Add(settings.CoverageFormat);
            arguments.Add(CoverageOutputArgument);
            arguments.Add(ResolvePath(root, settings.CoverageOutput));
        }
        if (!string.IsNullOrWhiteSpace(settings.Filter))
        {
            arguments.Add(TestFilterArgument);
            arguments.Add(settings.Filter);
        }
        return [.. arguments];
    }

    private static string ResolvePath(string root, string path)
        => Path.GetFullPath(path, root);
}
