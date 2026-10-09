using System.Text.Json;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Runs only the explicitly configured owning offline restore, leaving restored dispatch paused.</summary>
internal static class CliClusterRestore
{
    internal static Task RunAsync()
    {
        var storage = CliStorageConfiguration.Read();
        var restore = ClusterRestoreConfigurationBinding.Read();
        var limits = ClusterRestoreConfigurationBinding.ReadLimits();
        var receipt = ClusterRestoreCoordinator.Run(restore, storage.Storage, limits, TimeProvider.System,
            CancellationToken.None);
        Console.WriteLine(JsonSerializer.Serialize(receipt, JsonDefaults.Options));
        return Task.CompletedTask;
    }
}
