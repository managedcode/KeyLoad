using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record VerifiedBackupCutSnapshot(string Path, NativeCatalogBackupCapture Capture,
    string[] PartitionRows, IReadOnlyDictionary<string, byte[]> Archive,
    Command<DatabaseCommand>? SecondOperation = null, Result<object>? SecondResult = null);
