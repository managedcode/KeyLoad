using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.BackupRestore;

internal sealed record VerifiedBackupCutSnapshot(string Path, (StoreIdentity Identity, long Position) Capture,
    string[] PartitionRows, IReadOnlyDictionary<string, byte[]> Archive,
    Command<DatabaseCommand>? SecondOperation = null, Result<object>? SecondResult = null);
