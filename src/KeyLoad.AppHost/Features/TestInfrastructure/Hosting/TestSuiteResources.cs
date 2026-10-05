using KeyLoad.AppHost.Features.StorageRecovery;
using KeyLoad.AppHost.Features.TestInfrastructure.Execution;
using KeyLoad.AppHost.Features.TestInfrastructure.Validation;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteResources
{
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
            .WithEnvironment(TestSuiteSettings.VectorProfileEnvironment, EmptyText);
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
                .WithEnvironment("GITHUB_SHA", string.Empty)
                .WithEnvironment("GITHUB_ACTIONS", string.Empty);
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
            runner.WithEnvironment("Benchmarks__Target", settings.ComparisonTarget);
        }
        if (settings.ScaleProfile is not null)
        {
            runner.WithEnvironment("Benchmarks__ScaleProfile", settings.ScaleProfile.Id);
        }
        if (settings.VectorProfile is not null)
        {
            runner.WithEnvironment("Benchmarks__VectorProfile", settings.VectorProfile.Id);
        }
        if (settings.Suite == TestSuiteProtocol.ScalarUnitSuite)
        {
            runner.WithEnvironment("DOTNET_EnableHWIntrinsic", AddValueText);
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
            "--no-build", "--no-restore", "--configuration", "Release",
            "--results-directory", resultsDirectory
        };
        if (settings.ReportTrx)
        {
            arguments.Add(ItemText);
        }
        if (settings.CoverageSettings is not null && settings.CoverageOutput is not null)
        {
            arguments.Add(BuildArgumentsItemText);
            arguments.Add("--coverage-settings");
            arguments.Add(ResolvePath(root, settings.CoverageSettings));
            arguments.Add("--coverage-output-format");
            arguments.Add("cobertura");
            arguments.Add("--coverage-output");
            arguments.Add(ResolvePath(root, settings.CoverageOutput));
        }
        if (!string.IsNullOrWhiteSpace(settings.Filter))
        {
            arguments.Add("--treenode-filter");
            arguments.Add(settings.Filter);
        }
        return [.. arguments];
    }

    private static string ResolvePath(string root, string path)
        => Path.GetFullPath(path, root);
}
