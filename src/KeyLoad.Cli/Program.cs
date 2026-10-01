using System.Text.Json;
using KeyLoad;
using KeyLoad.Artifacts;
using KeyLoad.Client;
using KeyLoad.Storage.ZoneTree;

try
{
    if (args.Length == 0) { Help(); return; }
    switch (args[0])
    {
        case "backup" when args.Length == 3:
            using (var store = new ZoneTreeStore(new(Path.GetFullPath(args[1])))) store.CreateBackup(Path.GetFullPath(args[2]));
            Console.WriteLine("Backup verified at creation."); break;
        case "compact" when args.Length == 2:
            using (var store = new ZoneTreeStore(new(Path.GetFullPath(args[1]))))
                Console.WriteLine(JsonSerializer.Serialize(store.Compact(), JsonDefaults.Options));
            break;
        case "restore" when args.Length == 3:
            var identity = ZoneTreeStore.Restore(Path.GetFullPath(args[1]), Path.GetFullPath(args[2]));
            Console.WriteLine(JsonSerializer.Serialize(new { identity.NodeId, identity.Incarnation, identity.DispatchPaused }, JsonDefaults.Options)); break;
        case "pack-backup" when args.Length == 3: BackupArtifact.Pack(args[1], args[2]); Console.WriteLine("Archive created."); break;
        case "inspect-artifact" when args.Length == 2:
            Console.WriteLine(JsonSerializer.Serialize(BackupArtifact.Inspect(args[1]).Select(item => new { item.Name, item.Bytes, item.Pieces }), JsonDefaults.Options)); break;
        case "copy-artifact" when args.Length == 3:
            var copied = await ArtifactTransfer.CopyToFileStorageAsync(args[1], args[2]);
            if (copied.IsFailed) throw Errors.Fail(ErrorCode.RecoveryRequired, "Archive transfer failed.");
            Console.WriteLine("Archive copied through ManagedCode.Storage."); break;
        case "unpack-backup" when args.Length == 3: BackupArtifact.Unpack(args[1], args[2]); Console.WriteLine("Backup extracted; restore verifies its canonical checksums."); break;
        case "status" when args.Length is 2 or 3:
            var key = Environment.GetEnvironmentVariable("KEYLOAD_API_KEY") ?? (args.Length == 3 ? ProfileKey(args[2]) : null)
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, "Set KEYLOAD_API_KEY or supply a local profile path.");
            using (var http = new HttpClient { BaseAddress = new Uri(args[1]), Timeout = TimeSpan.FromSeconds(30) })
            {
                var result = await new KeyLoadClient(http, key).StatusAsync();
                if (result.IsFailed) throw Errors.Fail(Enum.TryParse<ErrorCode>(result.Problem.ErrorCode, out var code) ? code : ErrorCode.OwnershipLost,
                    result.Problem.Detail ?? "The node is unavailable.");
                Console.WriteLine(JsonSerializer.Serialize(result.Value, JsonDefaults.Options));
            }
            break;
        default: Help(); Environment.ExitCode = 2; break;
    }
}
catch (KeyLoadException exception) { Console.Error.WriteLine(JsonSerializer.Serialize(exception.ToProblem(), JsonDefaults.Options)); Environment.ExitCode = 1; }
static string ProfileKey(string path)
{
    using var profile = JsonDocument.Parse(File.ReadAllBytes(path)); return profile.RootElement.GetProperty("AdminKey").GetString()!;
}
static void Help() => Console.WriteLine("""
KeyLoad CLI
  status <node-url> [local-profile.json]
  backup <offline-database-directory> <empty-backup-directory>
  compact <offline-database-directory>
  restore <backup-directory> <empty-database-directory>
  pack-backup <backup-directory> <new-artifact-file>
  inspect-artifact <artifact-file>
  copy-artifact <artifact-file> <storage-directory>
  unpack-backup <artifact-file> <empty-backup-directory>

Backup and restore require an offline database. Restore creates a new incarnation with dispatch paused.
""");
