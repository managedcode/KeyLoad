namespace KeyLoad.Clustering;

/// <summary>One API credential's verified principal, read from storage once and then served from memory.</summary>
[Orleans.Alias(UserGrainProtocol.GrainAlias), Orleans.CodeGeneration.Version(UserGrainProtocol.InterfaceVersion)]
public interface IUserGrain : Orleans.IGrainWithStringKey
{
    /// <summary>Verifies the complete secret of this credential and returns its principal.</summary>
    /// <param name="secret">The complete API key secret; it is never part of the grain key.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The principal the credential authenticates.</returns>
    [Orleans.Alias(UserGrainProtocol.AuthenticateAlias)]
    Task<PrincipalRecord> AuthenticateAsync(string secret, CancellationToken cancellationToken);

    /// <summary>Forgets the cached credential after a change to it.</summary>
    /// <returns>Completion after the cache is cleared.</returns>
    [Orleans.Alias(UserGrainProtocol.InvalidateAlias), Orleans.Concurrency.AlwaysInterleave]
    Task InvalidateAsync();
}
