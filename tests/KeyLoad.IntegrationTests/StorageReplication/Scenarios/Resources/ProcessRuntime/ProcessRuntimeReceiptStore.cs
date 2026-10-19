using System.Globalization;
using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.StorageReplication;

/// <summary>Persists the original execution process fault and exact compilation evidence.</summary>
internal static class ProcessRuntimeReceiptStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static async Task WriteAsync(ProcessRuntimeRestartReceipt receipt, CancellationToken cancellationToken)
    {
        await Assert.That(receipt.KillStartedAtUtc.Offset).IsEqualTo(TimeSpan.Zero);
        await Assert.That(receipt.KillCompletedAtUtc >= receipt.KillStartedAtUtc).IsTrue();
        await Assert.That(receipt.KillStartedAtUtc > receipt.Before.StartedAtUtc).IsTrue();
        await Assert.That(receipt.KillCompletedAtUtc < receipt.After.StartedAtUtc).IsTrue();
        await Assert.That(receipt.Before.Binary).IsEqualTo(receipt.After.Binary);
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        if (await ProcessRuntimeOwnedReceipt.TryWriteAsync(receipt, json, cancellationToken))
        {
            return;
        }
        var directory = Path.Combine(receipt.RepositoryRoot, ProcessRuntimeProtocol.ArtifactsDirectory,
            ProcessRuntimeProtocol.QualificationDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, string.Format(CultureInfo.InvariantCulture,
            ProcessRuntimeProtocol.ReceiptFileName, receipt.Scenario, receipt.ResourceName));
        await File.WriteAllTextAsync(path, json, cancellationToken);
    }
}
