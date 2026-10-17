using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

[JsonConverter(typeof(JsonStringEnumConverter<ClusterRestoreRf3FailureCause>))]
internal enum ClusterRestoreRf3FailureCause
{
    Unknown
}
