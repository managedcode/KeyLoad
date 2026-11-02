using System.Security.Cryptography;
using KeyLoad.Core;
using Orleans.Concurrency;

namespace KeyLoad.Clustering;

/// <summary>Verifies one API credential against storage once, then authenticates later requests from memory.</summary>
[Reentrant]
public sealed class UserGrain(GrainRequestCodec codec, TimeProvider clock) : Grain, IUserGrain
{
    private Task<UserCredential>? loaded;

    /// <inheritdoc />
    public async Task<PrincipalRecord> AuthenticateAsync(string secret, CancellationToken cancellationToken)
    {
        var witness = AuthorizationIdentity.IssueCredentialWitness(secret);
        if (!string.Equals(witness.CredentialId, this.GetPrimaryKeyString(), StringComparison.Ordinal))
        {
            throw Unavailable();
        }
        var current = loaded ??= LoadAsync(secret, witness, cancellationToken);
        UserCredential credential;
        try
        {
            credential = await current.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        catch (Exception) when (current.IsFaulted || current.IsCanceled)
        {
            if ((ReferenceEquals(loaded, current) /* KLD0041: Clear only the failed user load task instance still cached by this grain through CLR reference identity. */))
            {
                loaded = null;
            }
            throw;
        }
        var now = clock.GetUtcNow();
        if (!CryptographicOperations.FixedTimeEquals(credential.Digest, witness.Digest.Span)
            || credential.ExpiresAt <= now)
        {
            throw Unavailable();
        }
        return credential.Principal;
    }

    /// <inheritdoc />
    public Task InvalidateAsync()
    {
        loaded = null;
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    private async Task<UserCredential> LoadAsync(string secret, DatabaseCredentialWitness witness,
        CancellationToken cancellationToken)
    {
        var state = ((RequestContext.Get(GrainRequestStreamProtocol.ContextKey) /* KLD0041: The native Orleans RequestContext API uses object; validate or restore this call-local request identity value without changing its original type. */) as GrainRequestContextState /* KLD0042: Validate the native Orleans RequestContext object as KeyLoad.Clustering.GrainRequestContextState? before using call-local identity or owner state. */)
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        var envelope = codec.CreateRead(state.RequestId, null, GrainReadKind.Authenticate, secret);
        using var placement = GrainRequestIdentityScope.ForPlacement(envelope,
            GrainIdentityContext.RequireOriginSilo(), cancellationToken);
        var reply = await GrainFactory.GetGrain<IConnectionGrain>(state.ConnectionId)
            .ExecuteAsync(envelope, cancellationToken).ConfigureAwait(true);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? DatabaseCredentialValidation.UnavailableCredentialDetail);
        }
        var authenticated = ((GrainReplyFactory.RequireValue(reply).Value /* KLD0041: Read the current generated GrainValue field only to validate the concrete RPC response or its permitted absence. */) as CredentialAuthentication /* KLD0042: Validate the current generated GrainValue slot as KeyLoad.Clustering.CredentialAuthentication? before accepting the concrete response from this RPC. */)
            ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        return new(witness.Digest.ToArray(), authenticated.Principal, authenticated.ExpiresAt);
    }

    private static KeyLoadException Unavailable()
        => Errors.Fail(ErrorCode.Unauthenticated, DatabaseCredentialValidation.UnavailableCredentialDetail);

    private sealed record UserCredential(byte[] Digest, PrincipalRecord Principal, DateTimeOffset? ExpiresAt);
}
