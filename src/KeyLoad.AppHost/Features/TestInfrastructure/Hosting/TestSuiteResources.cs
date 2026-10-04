using KeyLoad.AppHost.Features.StorageRecovery;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

internal static class TestSuiteResources
{
    internal static void Add(IDistributedApplicationBuilder builder, TestSuiteSettings settings)
    {
        var root = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../.."));
        if (!File.Exists(Path.Combine(root, "KeyLoad.slnx")))
        {
            throw new InvalidOperationException("The test AppHost must run from the KeyLoad source checkout.");
        }
        var resultsDirectory = ResolvePath(root, settings.ResultsDirectory ?? Path.Combine("TestResults", settings.Suite));
        var arguments = new List<string>
        {
            "test", "--project", Path.Combine(root, "tests", settings.Project),
            "--no-build", "--no-restore", "--configuration", "Release",
            "--results-directory", resultsDirectory
        };
        if (settings.ReportTrx)
        {
            arguments.Add("--report-trx");
        }
        if (settings.CoverageSettings is not null && settings.CoverageOutput is not null)
        {
            arguments.Add("--coverage");
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
        var runner = builder.AddExecutable(settings.ResourceName, "dotnet", root, [.. arguments])
            .WithEnvironment(TestSuiteSettings.SuiteEnvironment, "");
        if (settings.Suite == PriorProbeResources.RecoverySuite)
        {
            var probesDirectory = PriorProbeResources.CreateDirectoryPath(resultsDirectory);
            foreach (var preparation in PriorProbeResources.Add(builder, root, probesDirectory))
            {
                runner.WaitForCompletion(preparation);
            }
            runner.WithEnvironment(PriorProbeResources.DirectoryEnvironment, probesDirectory);
        }
        if (settings.Suite == "comparison" && settings.ComparisonTarget is not null)
        {
            runner.WithEnvironment("Benchmarks__Target", settings.ComparisonTarget);
        }
        if (settings.Suite == "unit-scalar")
        {
            runner.WithEnvironment("DOTNET_EnableHWIntrinsic", "0");
        }
    }

    private static string ResolvePath(string root, string path)
        => Path.GetFullPath(path, root);
}
