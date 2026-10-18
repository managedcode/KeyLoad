using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageCliCollector
{
    private const string CliProductAssemblyPattern = "KeyLoad.*.dll";
    private const string CollectCommand = "collect";
    private const string SettingsOption = "--settings";
    private const string IncludeFilesOption = "--include-files";
    private const string OutputOption = "--output";
    private const string FormatOption = "--output-format";
    private const string CoverageFormat = "coverage";
    private const string DisableConsoleOutput = "--disable-console-output";
    private const string NoLogo = "--nologo";

    internal static async Task<NativeCoverageMergeProcess.ChildResult> CollectCliAsync(
        NativeCoverageMergeProcess.ToolingOptions options,
        string ownedRoot, NativeCoverageDeploymentSnapshot originalSource, string imageName,
        IReadOnlyList<string> cliArguments, string reportPath, CancellationToken cancellationToken)
    {
        var limits = options.Coverage;
        var failures = new List<Exception>();
        var processResult = await RunCollectionAsync(options, ownedRoot, originalSource, imageName,
            cliArguments, reportPath, failures, cancellationToken).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        var result = NativeCoverageMergeProcess.ConvertResult(processResult
                                                              ?? throw new InvalidOperationException(NativeCoverageMergeProcess.StartFailure));
        ValidateReport(reportPath, limits);
        return result;
    }

    private static async Task<NativeCoverageMergeChildProcess.Result?> RunCollectionAsync(
        NativeCoverageMergeProcess.ToolingOptions options, string ownedRoot,
        NativeCoverageDeploymentSnapshot originalSource, string imageName, IReadOnlyList<string> cliArguments,
        string reportPath, List<Exception> failures, CancellationToken cancellationToken)
    {
        var limits = options.Coverage;
        var source = NativeCoverageCliDeployment.CliOutput;
        NativeCoverageMergeChildProcess.Result? processResult = null;
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var copiedDirectory = PrepareCopy(ownedRoot, imageName, originalSource, source, limits, failures);
                if (copiedDirectory is not null)
                {
                    processResult = await RunCopiedCliAsync(options, copiedDirectory, cliArguments,
                        reportPath, failures, cancellationToken).ConfigureAwait(false);
                    await ObserveChildResultAsync(processResult, failures).ConfigureAwait(false);
                }
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            ServerFailureObserver.Observe(() => options.Settings.VerifyUnchanged(limits), failures);
            ServerFailureObserver.Observe(() => originalSource.VerifyUnchanged(source, limits.MaximumFiles,
                limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
                limits.ReadBufferBytes), failures);
        }
        return processResult;
    }

    private static async Task ObserveChildResultAsync(NativeCoverageMergeChildProcess.Result? processResult,
        List<Exception> failures)
    {
        if (processResult is null)
        {
            return;
        }
        var result = NativeCoverageMergeProcess.ConvertResult(processResult);
        await ServerFailureObserver.ObserveAsync(
            () => NativeCoverageMergeProcess.AssertSuccessfulChildAsync(result), failures).ConfigureAwait(false);
    }

    private static string? PrepareCopy(string ownedRoot, string imageName,
        NativeCoverageDeploymentSnapshot originalSource, string source, NativeCoverageExecutionOptions limits,
        List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => originalSource.VerifyUnchanged(source, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes), failures);
        if (failures.Count != 0)
        {
            return null;
        }
        string? copied = null;
        ServerFailureObserver.Observe(() => copied = NativeCoverageCliDeployment.CopyCliClosure(
            ownedRoot, imageName, limits), failures);
        if (copied is null || failures.Count != 0)
        {
            return null;
        }
        ServerFailureObserver.Observe(() => originalSource.VerifyCopyMatches(copied, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes), failures);
        ServerFailureObserver.Observe(() => originalSource.VerifyUnchanged(source, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes), failures);
        return failures.Count == 0 ? copied : null;
    }

    private static async Task<NativeCoverageMergeChildProcess.Result?> RunCopiedCliAsync(
        NativeCoverageMergeProcess.ToolingOptions options, string copiedDirectory,
        IReadOnlyList<string> cliArguments, string reportPath, List<Exception> failures,
        CancellationToken cancellationToken)
    {
        var limits = options.Coverage;
        var cli = Path.Combine(copiedDirectory, NativeCoverageMergeProcess.CliAssemblyName);
        var assembly = new FileInfo(cli);
        if (!assembly.Exists || assembly.Length <= 0 || assembly.Length > limits.MaximumFileBytes
            || assembly.LinkTarget is not null || (assembly.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            failures.Add(new InvalidDataException("The copied Release KeyLoad CLI assembly is missing or unsafe."));
            return null;
        }
        var start = CreateCollectStart(options, copiedDirectory, cli, cliArguments, reportPath);
        ServerFailureObserver.Observe(() => options.Settings.VerifyUnchanged(limits), failures);
        if (failures.Count != 0)
        {
            return null;
        }
        NativeCoverageMergeChildProcess.Result? result = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            result = await NativeCoverageMergeChildProcess.RunAsync(start, options.Tests.OrdinaryTimeout,
                    options.Tests.ProcessSettlementTimeout, options.Tests.CleanupOutputCharacters, cancellationToken)
                .ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        return result;
    }

    private static void ValidateReport(string reportPath, NativeCoverageExecutionOptions options)
    {
        var info = new FileInfo(reportPath);
        if (!info.Exists || info.Length <= 0 || info.Length > options.MaximumReportBytes
            || info.LinkTarget is not null || (info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure);
        }
    }

    private static ProcessStartInfo CreateCollectStart(NativeCoverageMergeProcess.ToolingOptions options, string copiedDirectory,
        string cli, IReadOnlyList<string> cliArguments, string reportPath)
    {
        var includeFiles = Path.Combine(copiedDirectory, CliProductAssemblyPattern);
        if (includeFiles.Length > options.Coverage.MaximumPathCharacters)
        {
            throw new InvalidDataException("The native static-instrumentation file pattern exceeds its path bound.");
        }
        var start = NativeCoverageMergeChildProcess.CreateStartInfo(NativeCoverageMergeProcess.DotnetCommand,
            options.Tests.CleanupOutputCharacters, options.Coverage);
        start.WorkingDirectory = copiedDirectory;
        start.ArgumentList.Add(Path.Combine(options.Tool.PackageRoot, "tools/net8.0/any/dotnet-coverage.dll"));
        start.ArgumentList.Add(CollectCommand);
        start.ArgumentList.Add(SettingsOption);
        start.ArgumentList.Add(options.Settings.Path);
        start.ArgumentList.Add(IncludeFilesOption);
        start.ArgumentList.Add(includeFiles);
        start.ArgumentList.Add(OutputOption);
        start.ArgumentList.Add(reportPath);
        start.ArgumentList.Add(FormatOption);
        start.ArgumentList.Add(CoverageFormat);
        start.ArgumentList.Add(DisableConsoleOutput);
        start.ArgumentList.Add(NoLogo);
        start.ArgumentList.Add(NativeCoverageMergeProcess.DotnetCommand);
        start.ArgumentList.Add(cli);
        foreach (var argument in cliArguments)
        {
            start.ArgumentList.Add(argument);
        }
        return start;
    }
}
