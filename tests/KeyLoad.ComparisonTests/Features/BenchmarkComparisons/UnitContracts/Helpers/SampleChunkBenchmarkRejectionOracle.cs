using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class SampleChunkBenchmarkRejectionOracle
{
    private const string NodeExecutable = "node";
    private const string ReportModule = "scripts/Features/BenchmarkComparisons/sample-chunk-development-report.mjs";
    private const string QualificationDirectory = "artifacts/qualification";
    private const string CaptureDirectoryPrefix = "sample-chunk-rejection-host-";
    private const string NodeModuleArgument = "--input-type=module";
    private const string EvalArgument = "--eval";

    internal static async Task VerifyAsync(string evidenceDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        var root = EmbeddedBenchmarkProcess.RepositoryRoot();
        var evidence = Path.GetFullPath(evidenceDirectory);
        RequireEvidencePath(root, evidence);
        var capture = CreateCaptureDirectory(root);
        var startInfo = CreateStartInfo(root, evidence);
        var exitCode = await NativeSerializationBenchmarkProcess.RunProcessAsync(startInfo, capture,
            cancellationToken);
        if (exitCode != 0)
        {
            throw new InvalidOperationException("The sample-chunk rejection oracle child failed.");
        }
    }

    private static ProcessStartInfo CreateStartInfo(string root, string evidence)
    {
        var startInfo = new ProcessStartInfo(NodeExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = root
        };
        startInfo.ArgumentList.Add(NodeModuleArgument);
        startInfo.ArgumentList.Add(EvalArgument);
        startInfo.ArgumentList.Add(SampleChunkBenchmarkRejectionProgram.Source);
        startInfo.ArgumentList.Add(new Uri(Path.Combine(root, ReportModule)).AbsoluteUri);
        startInfo.ArgumentList.Add(evidence);
        return startInfo;
    }

    private static string CreateCaptureDirectory(string root)
    {
        var parent = Path.Combine(root, QualificationDirectory);
        var path = Path.Combine(parent, CaptureDirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void RequireEvidencePath(string root, string evidence)
    {
        var qualification = Path.GetFullPath(Path.Combine(root, QualificationDirectory)) + Path.DirectorySeparatorChar;
        if (!evidence.StartsWith(qualification, StringComparison.Ordinal)
            || !Directory.Exists(evidence))
        {
            throw new ArgumentException("Evidence must be an existing test-owned qualification directory.",
                nameof(evidence));
        }
    }
}
