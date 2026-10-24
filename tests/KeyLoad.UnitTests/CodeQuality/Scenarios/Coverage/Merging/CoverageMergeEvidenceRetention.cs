using KeyLoad.Server;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.CodeQuality;

internal static class CoverageMergeEvidenceRetention
{
    private const string EvidenceDirectoryPrefix = "execution-coverage-merge-";

    internal static async Task RunBeforeFixtureCleanupAsync(Func<Task> operation, string runtimeEvidenceRoot,
        string resultsDirectory, ITestOutput output, CoverageExecutionOptions options)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            () => PreserveAsync(runtimeEvidenceRoot, resultsDirectory, output, options), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PreserveAsync(string runtimeEvidenceRoot, string resultsDirectory,
        ITestOutput output, CoverageExecutionOptions options)
    {
        var runtimeRootCore = new DirectoryInfo(Path.GetFullPath(runtimeEvidenceRoot));
        if (!runtimeRootCore.Exists)
        {
            if (runtimeRootCore.LinkTarget is not null || File.Exists(runtimeRootCore.FullName))
            {
                throw new InvalidDataException("The execution coverage evidence root is not a regular directory.");
            }
            return;
        }
        var files = CoverageMergeEvidenceInventory.Read(runtimeRootCore.FullName, options);
        if (files.Count == 0)
        {
            return;
        }
        var results = new DirectoryInfo(Path.GetFullPath(resultsDirectory));
        if (!results.Exists)
        {
            Directory.CreateDirectory(results.FullName);
            results.Refresh();
        }
        if (results.LinkTarget is not null || (results.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The execution coverage results directory is unavailable or linked.");
        }
        var retainedRoot = CreateEvidenceDirectory(results.FullName);
        var failures = new List<Exception>();
        foreach (var file in files)
        {
            await ServerFailureObserver.ObserveAsync(
                () => CoverageMergeEvidenceCopier.CopyAndAttachAsync(file, retainedRoot, output, options),
                failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string CreateEvidenceDirectory(string resultsDirectory)
    {
        var path = Path.Combine(resultsDirectory, EvidenceDirectoryPrefix + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException("The unique execution coverage evidence destination already exists.");
        }
        Directory.CreateDirectory(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || directory.LinkTarget is not null
            || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The execution coverage evidence destination could not be owned safely.");
        }
        return directory.FullName;
    }
}
