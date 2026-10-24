namespace KeyLoad.UnitTests.CodeQuality;

internal static class CoverageCliDeployment
{
    private const string SolutionFile = "KeyLoad.slnx";
    internal const string CliOutputRelativePath = "src/KeyLoad.Cli/bin/Debug/net10.0";

    internal static string CliOutput => Path.Combine(RepositoryRoot, CliOutputRelativePath);
    internal static string RepositoryRoot { get; } = FindRepositoryRoot();

    internal static string CopyCliClosure(string destinationRoot, string name,
        CoverageExecutionOptions options)
        => CoverageDeploymentClosure.CopyToRoot(CliOutput, destinationRoot, name, options);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException("KeyLoad.slnx was not found above the test assembly.");
    }
}
