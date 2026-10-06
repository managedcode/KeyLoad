using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.CodeQuality;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.UnitTests.Features.BackupRestore;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageMergeProcess
{
    private const string DotnetCommand = "dotnet";
    private const string PowerShellCommand = "pwsh";
    private const string SolutionFile = "KeyLoad.slnx";
    private const string CliAssembly = "src/KeyLoad.Cli/bin/Release/net10.0/KeyLoad.Cli.dll";
    private const string MergeScript = "scripts/Features/CodeQuality/functional-coverage.native-merge.ps1";
    private const string CollectCommand = "collect";
    private const string CoverageFormat = "coverage";
    private const string OutputOption = "--output";
    private const string FormatOption = "--output-format";
    private const string DisableConsoleOutput = "--disable-console-output";
    private const string NoLogo = "--nologo";
    internal const string OutputFailure = "The native coverage tooling child output exceeded its bound.";
    private const string StartFailure = "The native coverage tooling child process did not start.";
    private const string ToolingInputDescriptorName = "tooling-inputs.v1.json";

    internal sealed record ToolingOptions(NativeCoverageToolPackage Tool, NativeCoverageExecutionOptions Coverage,
        TestExecutionOptions Tests, string RepositoryRoot);

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
        return new(NativeCoverageToolPackage.Read(), coverage, testOptions, FindRepositoryRoot());
    }

    internal static async Task<ChildResult> CollectCliAsync(ToolingOptions options, IReadOnlyList<string> cliArguments,
        string reportPath, CancellationToken cancellationToken)
    {
        var cli = Path.Combine(options.RepositoryRoot, CliAssembly);
        var start = NativeCoverageMergeChildProcess.CreateStartInfo(DotnetCommand, options.Tests.CleanupOutputCharacters, options.Coverage);
        start.ArgumentList.Add(Path.Combine(options.Tool.PackageRoot, "tools/net8.0/any/dotnet-coverage.dll"));
        start.ArgumentList.Add(CollectCommand);
        start.ArgumentList.Add(OutputOption);
        start.ArgumentList.Add(reportPath);
        start.ArgumentList.Add(FormatOption);
        start.ArgumentList.Add(CoverageFormat);
        start.ArgumentList.Add(DisableConsoleOutput);
        start.ArgumentList.Add(NoLogo);
        start.ArgumentList.Add(DotnetCommand);
        start.ArgumentList.Add(cli);
        foreach (var argument in cliArguments)
        { start.ArgumentList.Add(argument); }
        var processResult = await NativeCoverageMergeChildProcess.RunAsync(start, options.Tests.OrdinaryTimeout,
            options.Tests.ProcessSettlementTimeout, options.Tests.CleanupOutputCharacters, cancellationToken).ConfigureAwait(false);
        var result = ConvertResult(processResult);
        await AssertSuccessfulChildAsync(result).ConfigureAwait(false);
        var info = new FileInfo(reportPath);
        if (!info.Exists || info.Length <= 0 || info.Length > options.Coverage.MaximumReportBytes || info.LinkTarget is not null)
        { throw new InvalidDataException(OutputFailure); }
        return result;
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

    internal static string HashFile(string path, NativeCoverageExecutionOptions options)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.Length <= 0 || before.Length > options.MaximumReportBytes || before.LinkTarget is not null)
        { throw new InvalidDataException(OutputFailure); }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != before.Length)
        { throw new InvalidDataException(OutputFailure); }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[options.ReadBufferBytes];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
            if (length > options.MaximumReportBytes)
            { throw new InvalidDataException(OutputFailure); }
            digest.AppendData(buffer, 0, read);
        }
        var after = new FileInfo(path);
        if (length != before.Length || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        { throw new InvalidDataException(OutputFailure); }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void AddArgument(ProcessStartInfo start, string name, string value)
    {
        start.ArgumentList.Add("-" + name);
        start.ArgumentList.Add(value);
    }

    private static ChildResult ConvertResult(NativeCoverageMergeChildProcess.Result result) => new(
        result.ExitCode, result.StandardOutput, result.StandardError, result.ExitJoined, result.OutputJoined,
        result.ErrorJoined, result.Disposed);

    private static async Task AssertSuccessfulChildAsync(ChildResult result)
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
            if (File.Exists(Path.Combine(directory.FullName, SolutionFile)) && File.Exists(Path.Combine(directory.FullName, CliAssembly)))
            { return directory.FullName; }
        }
        throw new DirectoryNotFoundException(StartFailure);
    }
}
