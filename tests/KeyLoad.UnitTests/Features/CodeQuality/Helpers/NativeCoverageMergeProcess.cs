using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.BackupRestore;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeProcess
{
    internal const string DotnetCommand = "dotnet";
    private const string PowerShellCommand = "pwsh";
    private const string SolutionFile = "KeyLoad.slnx";
    internal const string CliAssemblyName = "KeyLoad.Cli.dll";
    private const string MergeScript = "scripts/Features/CodeQuality/functional-coverage.native-merge.ps1";
    internal const string OutputFailure = "The native coverage tooling child output exceeded its bound.";
    internal const string StartFailure = "The native coverage tooling child process did not start.";
    private const string ToolingInputDescriptorName = "tooling-inputs.v1.json";
    internal const string ProductAdmissionDescriptorEnvironment = "KEYLOAD_NATIVE_PRODUCT_DESCRIPTOR_PATH";
    internal const string ProductAdmissionRequiredEnvironment = "KEYLOAD_NATIVE_PRODUCT_ADMISSION_REQUIRED";
    internal const string ProductDescriptorName = "functional-coverage.native-product-descriptor.v1.json";

    internal sealed record ToolingOptions(NativeCoverageToolPackage Tool, NativeCoverageExecutionOptions Coverage,
        TestExecutionOptions Tests, string RepositoryRoot, NativeCoverageSettingsSnapshot Settings);

    internal sealed record ChildResult(int ExitCode, string StandardOutput, string StandardError,
        bool ExitJoined, bool OutputJoined, bool ErrorJoined, bool Disposed)
    {
        internal CliBackupRestoreProcessResult AsCliResult() => new(ExitCode, StandardOutput, StandardError,
            ExitJoined, OutputJoined, ErrorJoined, Disposed);
    }

    internal static ToolingOptions CaptureOptions()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        var coverage = AppHostOptionsRegistration.BindNativeCoverage(configuration).Value;
        if (!coverage.IsValid())
        { throw new InvalidOperationException(NativeCoverageExecutionOptions.ValidationMessage); }
        var testOptions = AppHostOptionsRegistration.BindTestExecution(configuration).Value;
        var repositoryRoot = FindRepositoryRoot();
        var settings = NativeCoverageSettingsSnapshot.Capture(repositoryRoot, coverage);
        return new(NativeCoverageToolPackage.Read(), coverage, testOptions, repositoryRoot, settings);
    }

    internal static NativeCoverageImageSourceSnapshot CaptureCliSource(ToolingOptions options)
    {
        var limits = options.Coverage;
        var source = NativeCoverageImageFixture.CliOutput;
        return NativeCoverageImageSourceSnapshot.Capture(source, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes);
    }

    internal static void VerifyCliSource(ToolingOptions options, NativeCoverageImageSourceSnapshot source)
    {
        var limits = options.Coverage;
        source.VerifyUnchanged(NativeCoverageImageFixture.CliOutput, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes);
    }

    internal static async Task<JsonDocument> MergeToolingReportsAsync(ToolingOptions options,
        string outputDirectory, IReadOnlyList<string> reports, CancellationToken cancellationToken)
    {
        var start = NativeCoverageMergeChildProcess.CreateStartInfo(PowerShellCommand, options.Tests.CleanupOutputCharacters, options.Coverage);
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(options.RepositoryRoot, MergeScript));
        AddArgument(start, "Mode", "ToolingProof");
        AddArgument(start, "Repository", options.RepositoryRoot);
        AddArgument(start, "EvidenceRoot", Path.GetDirectoryName(outputDirectory)!);
        AddArgument(start, "ToolPackageRoot", options.Tool.PackageRoot);
        AddArgument(start, "ToolVersion", options.Tool.Version);
        AddArgument(start, "OutputDirectory", outputDirectory);
        AddArgument(start, "TimeoutSeconds", ((int)options.Coverage.ApplicationCleanupTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumDescriptorBytes", options.Coverage.MaximumDescriptorBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumFiles", options.Coverage.MaximumFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "ReadBufferBytes", options.Coverage.ReadBufferBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumTotalBytes", options.Coverage.MaximumTotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumFileBytes", options.Coverage.MaximumFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumPathCharacters", options.Coverage.MaximumPathCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumManifestBytes", options.Coverage.MaximumManifestBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumReportBytes", options.Coverage.MaximumReportBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "SettlementTimeoutSeconds", ((int)options.Coverage.SettlementTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumOutputCharacters", options.Tests.CleanupOutputCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var evidenceRoot = Path.GetDirectoryName(Path.GetFullPath(outputDirectory))!;
        var descriptorPath = Path.Combine(evidenceRoot, ToolingInputDescriptorName);
        await NativeCoverageToolingInputDescriptorWriter.WriteAsync(descriptorPath, evidenceRoot, reports,
            options.Coverage, cancellationToken).ConfigureAwait(false);
        AddArgument(start, "ToolingInputDescriptor", descriptorPath);
        var processResult = await NativeCoverageMergeChildProcess.RunAsync(start, options.Coverage.ApplicationCleanupTimeout,
            options.Tests.ProcessSettlementTimeout, options.Tests.CleanupOutputCharacters, cancellationToken).ConfigureAwait(false);
        var result = ConvertResult(processResult);
        await AssertSuccessfulChildAsync(result).ConfigureAwait(false);
        return JsonDocument.Parse(result.StandardOutput);
    }

    internal static async Task<ChildResult> RunProductAdmissionAsync(ToolingOptions options,
        string evidenceRoot, string descriptorPath, CancellationToken cancellationToken)
    {
        var start = NativeCoverageMergeChildProcess.CreateStartInfo(PowerShellCommand,
            options.Tests.CleanupOutputCharacters, options.Coverage);
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-File");
        start.ArgumentList.Add(Path.Combine(options.RepositoryRoot, MergeScript));
        AddArgument(start, "Mode", "ProductAdmission");
        AddArgument(start, "Repository", options.RepositoryRoot);
        AddArgument(start, "EvidenceRoot", evidenceRoot);
        AddArgument(start, "DescriptorPath", descriptorPath);
        AddArgument(start, "ToolPackageRoot", options.Tool.PackageRoot);
        AddArgument(start, "ToolVersion", options.Tool.Version);
        AddArgument(start, "NativeOptionsJson", NativeCoverageProductEvidenceOptions.Read(evidenceRoot, options.Coverage));
        AddArgument(start, "MaximumDescriptorBytes", options.Coverage.MaximumDescriptorBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumFiles", options.Coverage.MaximumFiles.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "TimeoutSeconds", ((int)options.Coverage.ApplicationCleanupTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "ReadBufferBytes", options.Coverage.ReadBufferBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumTotalBytes", options.Coverage.MaximumTotalBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumFileBytes", options.Coverage.MaximumFileBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumPathCharacters", options.Coverage.MaximumPathCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumManifestBytes", options.Coverage.MaximumManifestBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumReportBytes", options.Coverage.MaximumReportBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "SettlementTimeoutSeconds", ((int)options.Coverage.SettlementTimeout.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(start, "MaximumOutputCharacters", options.Tests.CleanupOutputCharacters.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var result = await NativeCoverageMergeChildProcess.RunAsync(start, options.Coverage.ApplicationCleanupTimeout,
            options.Tests.ProcessSettlementTimeout, options.Tests.CleanupOutputCharacters, cancellationToken).ConfigureAwait(false);
        return ConvertResult(result);
    }

    internal static string HashFile(string path, NativeCoverageExecutionOptions options)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.Length <= 0 || before.Length > options.MaximumReportBytes || before.LinkTarget is not null)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != before.Length)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[options.ReadBufferBytes];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
            if (length > options.MaximumReportBytes)
            { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
            digest.AppendData(buffer, 0, read);
        }
        var after = new FileInfo(path);
        if (length != before.Length || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        { throw new InvalidDataException(NativeCoverageMergeProcess.OutputFailure); }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void AddArgument(ProcessStartInfo start, string name, string value)
    {
        start.ArgumentList.Add("-" + name);
        start.ArgumentList.Add(value);
    }

    internal static ChildResult ConvertResult(NativeCoverageMergeChildProcess.Result result) => new(
        result.ExitCode, result.StandardOutput, result.StandardError, result.ExitJoined, result.OutputJoined,
        result.ErrorJoined, result.Disposed);

    internal static async Task AssertSuccessfulChildAsync(ChildResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.StandardError);
        await Assert.That(result.ExitJoined).IsTrue();
        await Assert.That(result.OutputJoined).IsTrue();
        await Assert.That(result.ErrorJoined).IsTrue();
        await Assert.That(result.Disposed).IsTrue();
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile))
                && File.Exists(Path.Combine(directory.FullName, NativeCoverageImageFixture.CliOutputRelativePath,
                    CliAssemblyName)))
            { return directory.FullName; }
        }
        throw new DirectoryNotFoundException(StartFailure);
    }
}

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
        string ownedRoot, NativeCoverageImageSourceSnapshot originalSource, string imageName,
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
        NativeCoverageImageSourceSnapshot originalSource, string imageName, IReadOnlyList<string> cliArguments,
        string reportPath, List<Exception> failures, CancellationToken cancellationToken)
    {
        var limits = options.Coverage;
        var source = NativeCoverageImageFixture.CliOutput;
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
        { return; }
        var result = NativeCoverageMergeProcess.ConvertResult(processResult);
        await ServerFailureObserver.ObserveAsync(
            () => NativeCoverageMergeProcess.AssertSuccessfulChildAsync(result), failures).ConfigureAwait(false);
    }

    private static string? PrepareCopy(string ownedRoot, string imageName,
        NativeCoverageImageSourceSnapshot originalSource, string source, NativeCoverageExecutionOptions limits,
        List<Exception> failures)
    {
        ServerFailureObserver.Observe(() => originalSource.VerifyUnchanged(source, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes), failures);
        if (failures.Count != 0)
        { return null; }
        string? copied = null;
        ServerFailureObserver.Observe(() => copied = NativeCoverageImageFixture.CopyCliClosure(
            ownedRoot, imageName, limits), failures);
        if (copied is null || failures.Count != 0)
        { return null; }
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
        { return null; }
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
        { start.ArgumentList.Add(argument); }
        return start;
    }
}
