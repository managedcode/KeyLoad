using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Owns only this test's actual resources and observations; it is never a database or diagnostic payload.</summary>
internal sealed class FollowerDocumentRf3State(FollowerDocumentCaller mode, FollowerDocumentChange change)
{
    internal FollowerDocumentCaller Mode { get; } = mode;
    internal FollowerDocumentChange Change { get; } = change;
    internal string Root { get; set; } = string.Empty;
    internal bool RootOwned { get; set; }
    internal bool StartupAttempted { get; set; }
    internal List<Exception> Failures { get; } = [];
    internal RequestCqrsProbeFixture? Controls { get; set; }
    internal RequestCqrsRf3Wave? Wave { get; set; }
    internal RequestCqrsRf3Callers? Administrator { get; set; }
    internal RequestCqrsRf3Callers? Caller { get; set; }
    internal IReadOnlyList<ReplicaSiloDiscovery>? Discovery { get; set; }
    internal CancellationTokenSource? CallLifetime { get; set; }
    internal CancellationTokenSource? WaveLifetime { get; set; }
    internal Task<FollowerDocumentPublicObservation>? Pending { get; set; }
    internal Guid ArmId { get; set; }
    internal RequestCqrsProbeMarkerRecord Observed { get; set; }
    internal RequestCqrsPhaseFaultIdentity Identity { get; set; } = null!;
    internal string ReplicaId { get; set; } = string.Empty;
    internal NodeEpochRf3Profile Profile { get; set; } = null!;
    internal int SelectedIndex { get; set; }
    internal NodeStatus Before { get; set; } = null!;
    internal NodeStatus Changed { get; set; } = null!;
    internal AtomicPartitionPlacementResolution Placement { get; set; } = null!;
    internal PrincipalRecord Principal { get; set; } = null!;
    internal ApiKeyRecord Credential { get; set; } = null!;
    internal ReadFollowerDocumentRequestV1 Request { get; set; } = null!;
}
