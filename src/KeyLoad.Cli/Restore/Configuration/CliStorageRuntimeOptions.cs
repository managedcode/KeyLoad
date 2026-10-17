using KeyLoad.Storage.ZoneTree;
using KeyLoad.Storage.ZoneTree.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

internal sealed record CliStorageRuntimeOptions(
    IOptions<ZoneTreeStorageExecutionOptions> Storage, IOptions<ZoneTreePointCacheExecutionOptions> PointCache,
    IOptions<CliBackupExecutionOptions> Backup);
