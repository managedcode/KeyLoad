using System.Globalization;
using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;

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
    private const string CoberturaFormat = "cobertura";
    private const string CoverageOutputArgument = "--coverage-output";
    private const string TestFilterArgument = "--treenode-filter";

    internal static void Add(IDistributedApplicationBuilder builder, TestSuiteSettings settings)
    {
        const string Path2Text = "../..";
        const string AddPath2Text = "KeyLoad.slnx";
        const string MessageText = "The test AppHost must run from the KeyLoad source checkout.";
        const string Path1Text = "TestResults";
        const string CommandText = "dotnet";
        const string EmptyText = "";
        const string NameText = "KEYLOAD_LOCAL_RF3_IMAGE_CHILD";
        const string ValueText = "true";
        const string AddNameText = "KEYLOAD_IMAGE_RECEIPT";
        const string AddValueText = "0";

        var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, Path2Text));
        if (!File.Exists(Path.Combine(root, AddPath2Text)))
        {
            throw new InvalidOperationException(MessageText);
        }
        var resultsDirectory = ResolvePath(root, settings.ResultsDirectory ?? Path.Combine(Path1Text, settings.Suite));
        var arguments = BuildArguments(root, settings, resultsDirectory);
        var runner = builder.AddExecutable(settings.ResourceName, CommandText, root, arguments)
            .WithEnvironment(TestSuiteSettings.SuiteEnvironment, EmptyText)
            .WithEnvironment(TestSuiteSettings.ScaleProfileEnvironment, EmptyText)
            .WithEnvironment(TestSuiteSettings.VectorProfileEnvironment, EmptyText)
            .WithEnvironment(TestSuiteSelectionValidator.OpenLoopRateEnvironment, EmptyText);
        if (settings.LocalRf3ImageEnabled)
        {
            var execution = LocalRf3ImageExecution.Create(root);
            var preparation = LocalRf3ImagePrerequisite.Add(builder, root, execution);
            runner.WaitForCompletion(preparation);
            runner.WithEnvironment(LocalRf3ImageExecution.ProvenanceEnvironment, LocalRf3ImageExecution.Provenance)
                .WithEnvironment(LocalRf3ImageExecution.ImageReferenceEnvironment, execution.ImageReference)
                .WithEnvironment(LocalRf3ImageExecution.ReceiptEnvironment, execution.ReceiptPath)
                .WithEnvironment(NameText, ValueText)
                .WithEnvironment(LocalRf3ImageRequest.EnabledEnvironment, string.Empty)
                .WithEnvironment(AddNameText, string.Empty)
                .WithEnvironment(GithubRevisionEnvironment, string.Empty)
                .WithEnvironment(GithubActionsEnvironment, string.Empty);
        }
        if (settings.Suite == PriorProbeResources.RecoverySuite)
        {
            var probesDirectory = PriorProbeResources.CreateDirectoryPath(resultsDirectory);
            foreach (var preparation in PriorProbeResources.Add(builder, root, probesDirectory))
            {
                runner.WaitForCompletion(preparation);
            }
            runner.WithEnvironment(PriorProbeResources.DirectoryEnvironment, probesDirectory);
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
            runner.WithEnvironment(IntrinsicsEnvironment, AddValueText);
        }
    }

    private static string[] BuildArguments(string root, TestSuiteSettings settings, string resultsDirectory)
    {
        const string ResultText = "test";
        const string BuildArgumentsResultText = "--project";
        const string Path2Text = "tests";
        const string ItemText = "--report-trx";
        const string BuildArgumentsItemText = "--coverage";

        var arguments = new List<string>
        {
            ResultText, BuildArgumentsResultText, Path.Combine(root, Path2Text, settings.Project),
            NoBuildArgument, NoRestoreArgument, ConfigurationArgument, ReleaseConfiguration,
            ResultsDirectoryArgument, resultsDirectory
        };
        if (settings.ReportTrx)
        {
            arguments.Add(ItemText);
        }
        if (settings.CoverageSettings is not null && settings.CoverageOutput is not null)
        {
            arguments.Add(BuildArgumentsItemText);
            arguments.Add(CoverageSettingsArgument);
            arguments.Add(ResolvePath(root, settings.CoverageSettings));
            arguments.Add(CoverageFormatArgument);
            arguments.Add(CoberturaFormat);
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
