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
            if (ReferenceEquals(loaded, current))
            {
                loaded = null;
            }
            throw;
        }
        var now = clock.GetUtcNow();
        if (!CryptographicOperations.FixedTimeEquals(credential.Digest, witness.Digest.Span)
            || credential.ExpiresAt <= now || credential.Principal.ExpiresAt <= now)
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
        var state = RequestContext.Get(GrainRequestStreamProtocol.ContextKey) as GrainRequestContextState
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        var envelope = codec.CreateRead(state.RequestId, null, GrainReadKind.Authenticate, secret);
        var reply = await GrainFactory.GetGrain<IConnectionGrain>(state.ConnectionId)
            .ExecuteAsync(envelope, cancellationToken).ConfigureAwait(true);
        if (reply.Error is { } error)
        {
            throw Errors.Fail(error, reply.SafeDetail ?? DatabaseCredentialValidation.UnavailableCredentialDetail);
        }
        var authenticated = GrainReplyFactory.RequireValue(reply).Value as CredentialAuthentication
            ?? throw Errors.Fail(ErrorCode.Corruption, GrainRoutingProtocol.InvalidRequest);
        return new(witness.Digest.ToArray(), authenticated.Principal, authenticated.ExpiresAt);
    }

    private static KeyLoadException Unavailable()
        => Errors.Fail(ErrorCode.Unauthenticated, DatabaseCredentialValidation.UnavailableCredentialDetail);

    private sealed record UserCredential(byte[] Digest, PrincipalRecord Principal, DateTimeOffset? ExpiresAt);
}
