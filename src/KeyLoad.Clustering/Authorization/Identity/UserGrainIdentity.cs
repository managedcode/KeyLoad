namespace KeyLoad.Clustering;

/// <summary>Canonical addressing of a credential's user grain by its public credential ID, never by its secret.</summary>
internal static class UserGrainIdentity
{
    internal static string For(string credentialId)
        => !string.IsNullOrEmpty(credentialId) ? credentialId
            : throw new ArgumentException(GrainRoutingProtocol.InvalidRequest, nameof(credentialId));
}
