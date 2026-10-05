namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record MembershipAuthoritySettings
{
    public string Mode { get; init; } = MembershipAuthoritySettingsProtocol.Local;
    public Guid AuthorityPhysicalShardId { get; init; }
    public Guid AuthorityIncarnation { get; init; }
    public string[] AuthorityEndpoints { get; init; } = [];
    public string? AuthorityPeerSecret { get; init; }
    public Guid TrustedGroupPhysicalShardId { get; init; }
    public Guid TrustedGroupIncarnation { get; init; }
    public string[] TrustedGroupVoterIds { get; init; } = [];
    public string[] TrustedGroupSiloEndpoints { get; init; } = [];
    public string? TrustedGroupPeerSecret { get; init; }
}
