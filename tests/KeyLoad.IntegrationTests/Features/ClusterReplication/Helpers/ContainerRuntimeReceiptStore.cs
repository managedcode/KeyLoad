using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Persists the unchanged web-cased restart receipt alongside the actual source SHA.</summary>
internal static class ContainerRuntimeReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task<string> ReadSourceShaAsync(string repositoryRoot, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ContainerRuntimeProtocol.GitExecutable)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = repositoryRoot };
        startInfo.ArgumentList.Add(ContainerRuntimeProtocol.GitRevisionCommand);
        startInfo.ArgumentList.Add(ContainerRuntimeProtocol.GitHeadArgument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException(ContainerRuntimeProtocol.GitStartFailure);
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != ContainerRuntimeProtocol.SuccessfulExitCode)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.GitRevisionFailure, ContainerRuntimeDocker.Clip(error)));
        }
        return output.Trim();
    }

    internal static async Task WriteAsync(ContainerRuntimeRestartReceipt receipt, CancellationToken cancellationToken)
    {
        // AC-DIAG-003: these are actual SIGKILL action times, distinct from Docker's original start time.
        await Assert.That(receipt.KillStartedAtUtc.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(receipt.KillCompletedAtUtc >= receipt.KillStartedAtUtc).IsTrue();
        await Assert.That(receipt.KillStartedAtUtc > DateTimeOffset.Parse(receipt.BeforeStartedAt, CultureInfo.InvariantCulture)).IsTrue();
        await Assert.That(receipt.KillCompletedAtUtc < DateTimeOffset.Parse(receipt.AfterStartedAt, CultureInfo.InvariantCulture)).IsTrue();
        var outputDirectory = Path.Combine(receipt.RepositoryRoot, ContainerRuntimeProtocol.ArtifactsDirectory,
            ContainerRuntimeProtocol.QualificationDirectory);
        Directory.CreateDirectory(outputDirectory);
        var fileName = string.Format(CultureInfo.InvariantCulture, ContainerRuntimeProtocol.ReceiptFileName,
            receipt.Scenario, receipt.ResourceName);
        var outputPath = Path.Combine(outputDirectory, fileName);
        await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(receipt, JsonOptions), cancellationToken);
    }
}
