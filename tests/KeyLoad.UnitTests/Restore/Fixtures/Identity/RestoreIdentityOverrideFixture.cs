using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class RestoreIdentityOverrideFixture
{
    internal TestDatabase Source
    {
        get;
    }
    internal string BackupDirectory
    {
        get;
    }
    internal string TargetDirectory
    {
        get;
    }
    internal PartitionRef Partition => Source.Partition;

    internal RestoreIdentityOverrideFixture(string root)
    {
        BackupDirectory = Path.Combine(root, "backup");
        TargetDirectory = Path.Combine(root, "restored");
        Source = new(directory: Path.Combine(root, "source"));
    }

    internal ReplicatedOperation Batch(DatabaseEngine database, Guid id, params Mutation[] mutations)
        => new(id, OperationKind.Batch, RestoreIdentityOverrideState.Principal, database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new CommandRequest(id, Partition, [.. mutations]), JsonDefaults.Options));

    internal long CreateBackup() => Source.Store.CreateBackup(BackupDirectory);

    internal async Task<Dictionary<string, byte[]>> CaptureArchiveAsync()
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(BackupDirectory, "*", SearchOption.AllDirectories))
        {
            result.Add(Path.GetRelativePath(BackupDirectory, file), await File.ReadAllBytesAsync(file));
        }
        return result;
    }
}
