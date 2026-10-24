using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.BackupRestore;

internal sealed record CliStorageRuntimeOptions(
    IOptions<ZoneTreeStorageExecutionOptions> Storage, IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<CliBackupExecutionOptions> Backup);
