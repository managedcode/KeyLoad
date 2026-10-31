namespace KeyLoad.Clustering;

/// <summary>A verified API credential: its active principal and the credential's own expiry.</summary>
/// <param name="Principal">The persisted principal the credential authenticates.</param>
/// <param name="ExpiresAt">The credential expiry, or null when it does not expire.</param>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(UserGrainProtocol.AuthenticationAlias)]
public sealed record CredentialAuthentication(
    [property: global::Orleans.Id(0)] PrincipalRecord Principal,
    [property: global::Orleans.Id(1)] DateTimeOffset? ExpiresAt);
