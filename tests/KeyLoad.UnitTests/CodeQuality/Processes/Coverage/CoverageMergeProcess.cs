using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.UnitTests.Features.BackupRestore;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class CoverageMergeProcess
{
    internal const string DotnetCommand = "dotnet";
    private const string PowerShellCommand = "pwsh";
    private const string SolutionFile = "KeyLoad.slnx";
    internal const string CliAssemblyName = "KeyLoad.Cli.dll";
    private const string MergeScript = "scripts/Features/CodeQuality/functional-coverage.execution-merge.ps1";
    internal const string OutputFailure = "The execution coverage tooling child output exceeded its bound.";
    internal const string StartFailure = "The execution coverage tooling child process did not start.";
    private const string ToolingInputDescriptorName = "tooling-inputs.json";
    internal const string ProductAdmissionDescriptorEnvironment = "KEYLOAD__PRODUCT_DESCRIPTOR_PATH";
    internal const string ProductAdmissionRequiredEnvironment = "KEYLOAD__PRODUCT_ADMISSION_REQUIRED";
    internal const string ProductDescriptorName = "functional-coverage.execution-product-descriptor.json";

    internal sealed record ToolingOptions(CoverageToolPackage Tool, CoverageExecutionOptions Coverage,
        TestExecutionOptions Tests, string RepositoryRoot, CoverageSettingsSnapshot Settings);

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
        var coverage = AppHostOptionsRegistration.Bind<CoverageExecutionOptions>(configuration.GetSection(CoverageExecutionOptions.SectionName), value => value.IsValid(), CoverageExecutionOptions.ValidationMessage).Value;
        if (!coverage.IsValid())
        {
            throw new InvalidOperationException(CoverageExecutionOptions.ValidationMessage);
        }
        var testOptions = AppHostOptionsRegistration.BindTestExecution(configuration).Value;
        var repositoryRoot = FindRepositoryRoot();
        var settings = CoverageSettingsSnapshot.Capture(repositoryRoot, coverage);
        return new(CoverageToolPackage.Read(), coverage, testOptions, repositoryRoot, settings);
    }

    internal static CoverageDeploymentSnapshot CaptureCliSource(ToolingOptions options)
    {
        var limits = options.Coverage;
        var source = CoverageCliDeployment.CliOutput;
        return CoverageDeploymentSnapshot.Capture(source, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes);
    }

    internal static void VerifyCliSource(ToolingOptions options, CoverageDeploymentSnapshot source)
    {
        var limits = options.Coverage;
        source.VerifyUnchanged(CoverageCliDeployment.CliOutput, limits.MaximumFiles,
            limits.MaximumFileBytes, limits.MaximumTotalBytes, limits.MaximumPathCharacters,
            limits.ReadBufferBytes);
    }

    internal static async Task<JsonDocument> MergeToolingReportsAsync(ToolingOptions options,
        string outputDirectory, IReadOnlyList<string> reports, CancellationToken cancellationToken)
    {
        var start = CoverageMergeChildProcess.CreateStartInfo(PowerShellCommand, options.Tests.CleanupOutputCharacters, options.Coverage);
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
        await CoverageToolingInputDescriptorWriter.WriteAsync(descriptorPath, evidenceRoot, reports,
            options.Coverage, cancellationToken).ConfigureAwait(false);
        AddArgument(start, "ToolingInputDescriptor", descriptorPath);
        var processResult = await CoverageMergeChildProcess.RunAsync(start, options.Coverage.ApplicationCleanupTimeout,
            options.Tests.ProcessSettlementTimeout, options.Tests.CleanupOutputCharacters, cancellationToken).ConfigureAwait(false);
        var result = ConvertResult(processResult);
        await AssertSuccessfulChildAsync(result).ConfigureAwait(false);
        return JsonDocument.Parse(result.StandardOutput);
    }

    internal static async Task<ChildResult> RunProductAdmissionAsync(ToolingOptions options,
        string evidenceRoot, string descriptorPath, CancellationToken cancellationToken)
    {
        var start = CoverageMergeChildProcess.CreateStartInfo(PowerShellCommand,
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
        AddArgument(start, "OptionsJson", CoverageProductEvidenceOptions.Read(evidenceRoot, options.Coverage));
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
        var result = await CoverageMergeChildProcess.RunAsync(start, options.Coverage.ApplicationCleanupTimeout,
            options.Tests.ProcessSettlementTimeout, options.Tests.CleanupOutputCharacters, cancellationToken).ConfigureAwait(false);
        return ConvertResult(result);
    }

    internal static string HashFile(string path, CoverageExecutionOptions options)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.Length <= 0 || before.Length > options.MaximumReportBytes || before.LinkTarget is not null)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != before.Length)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[options.ReadBufferBytes];
        long length = 0;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            length += read;
            if (length > options.MaximumReportBytes)
            {
                throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
            }
            digest.AppendData(buffer, 0, read);
        }
        var after = new FileInfo(path);
        if (length != before.Length || after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            throw new InvalidDataException(CoverageMergeProcess.OutputFailure);
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static void AddArgument(ProcessStartInfo start, string name, string value)
    {
        start.ArgumentList.Add("-" + name);
        start.ArgumentList.Add(value);
    }

    internal static ChildResult ConvertResult(CoverageMergeChildProcess.Result result) => new(
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
                && File.Exists(Path.Combine(directory.FullName, CoverageCliDeployment.CliOutputRelativePath,
                    CliAssemblyName)))
            {
                return directory.FullName;
            }
        }
        throw new DirectoryNotFoundException(StartFailure);
    }
}
