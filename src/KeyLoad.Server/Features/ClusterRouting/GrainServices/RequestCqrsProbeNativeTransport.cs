namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Locates only exact original claims and directly awaits their addressed native disposal acknowledgements.</summary>
internal sealed class RequestCqrsProbeNativeTransport(RequestCqrsProbeNativeTrust trust,
    RequestCqrsProbeNativeAuthentication authentication, RequestCqrsProbeGrainServiceClient client)
{
    internal async ValueTask NotifyAsync(RequestCqrsProbeNativeIdentity identity, CancellationToken token)
    {
        var claims = new List<RequestCqrsProbeNativeClaim>();
        foreach (var owner in trust.Owners)
        {
            foreach (var voter in owner.VoterIds)
            {
                token.ThrowIfCancellationRequested();
                var address = trust.Resolve(owner, voter);
                var request = authentication.Create(identity, owner, voter, address.ToParsableString(), null);
                var reply = await client.ExchangeAsync(address, request, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var verified = authentication.VerifyReply(request, reply);
                if (verified.Claim is { } claim) { claims.Add(claim); }
            }
        }
        foreach (var claim in claims)
        {
            token.ThrowIfCancellationRequested();
            var address = trust.RequireCurrent(claim.Owner, claim.Voter, claim.SiloAddress);
            var request = authentication.Create(identity, claim.Owner, claim.Voter, claim.SiloAddress, claim);
            var reply = await client.ExchangeAsync(address, request, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            _ = authentication.VerifyReply(request, reply);
        }
    }
}
