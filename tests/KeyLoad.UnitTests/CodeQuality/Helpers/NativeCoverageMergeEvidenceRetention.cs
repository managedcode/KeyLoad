using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.Server;
using TUnit.Core.Interfaces;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeEvidenceRetention
{
    private const string EvidenceDirectoryPrefix = "native-coverage-merge-";

    internal static async Task RunBeforeFixtureCleanupAsync(Func<Task> operation, string nativeEvidenceRoot,
        string resultsDirectory, ITestOutput output, NativeCoverageExecutionOptions options)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            () => PreserveAsync(nativeEvidenceRoot, resultsDirectory, output, options), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task PreserveAsync(string nativeEvidenceRoot, string resultsDirectory,
        ITestOutput output, NativeCoverageExecutionOptions options)
    {
        var nativeRoot = new DirectoryInfo(Path.GetFullPath(nativeEvidenceRoot));
        if (!nativeRoot.Exists)
        {
            if (nativeRoot.LinkTarget is not null || File.Exists(nativeRoot.FullName))
            {
                throw new InvalidDataException("The native coverage evidence root is not a regular directory.");
            }
            return;
        }
        var files = NativeCoverageMergeEvidenceInventory.Read(nativeRoot.FullName, options);
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
            throw new InvalidDataException("The native coverage results directory is unavailable or linked.");
        }
        var retainedRoot = CreateEvidenceDirectory(results.FullName);
        var failures = new List<Exception>();
        foreach (var file in files)
        {
            await ServerFailureObserver.ObserveAsync(
                () => NativeCoverageMergeEvidenceCopier.CopyAndAttachAsync(file, retainedRoot, output, options),
                failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static string CreateEvidenceDirectory(string resultsDirectory)
    {
        var path = Path.Combine(resultsDirectory, EvidenceDirectoryPrefix + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path) || File.Exists(path))
        {
            throw new IOException("The unique native coverage evidence destination already exists.");
        }
        Directory.CreateDirectory(path);
        var directory = new DirectoryInfo(path);
        if (!directory.Exists || directory.LinkTarget is not null
            || (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException("The native coverage evidence destination could not be owned safely.");
        }
        return directory.FullName;
    }
}
