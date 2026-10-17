using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

[JsonConverter(typeof(JsonStringEnumConverter<ClusterRestoreRf3Phase>))]
internal enum ClusterRestoreRf3Phase
{
    Unknown, ProfileCreate, VerifiedImageRead, BuilderCreate, OriginalParameters, OriginalPeerKey,
    IsolationPrepare, BuildApplication, VerifyImageBeforeStart, ApplicationStart, SixHealthy,
    DataReadiness, VerifyStartedImages, StartupReady, SeedCreate, ScenarioEnter, ScenarioComplete,
    SeedDispose, SourceDispose
}
