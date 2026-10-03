using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

/// <summary>Discovers bounded authenticated runtime addresses without using the membership table.</summary>
public sealed class ReplicaSiloDiscoveryClient : IDisposable
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaPeerOptions options;
    private readonly ReplicaSiloDiscoveryState local;
    private readonly ReplicaEnvelopeAuthenticator authentication;
    private readonly TimeProvider clock;
    private readonly Dictionary<string, Uri> endpoints;
    private readonly ConcurrentDictionary<string, CachedAddress> cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim discoveryGate = new(1, 1);
    private readonly byte[] credential;
    private readonly HttpClient http;

    /// <summary>Creates the signed discovery client and a cache limited to configured voters.</summary>
    /// <param name="configuration">The local voter set, incarnation and cache freshness bound.</param>
    /// <param name="options">The fixed HTTP peer endpoints and cluster signing settings.</param>
    /// <param name="local">The local actual runtime generation used for self-resolution.</param>
    /// <param name="authentication">The shared request/reply and discovery verifier.</param>
    /// <param name="clock">The system clock and monotonic cache timer.</param>
    public ReplicaSiloDiscoveryClient(ReplicaConfiguration configuration, ReplicaPeerOptions options,
        ReplicaSiloDiscoveryState local, ReplicaEnvelopeAuthenticator authentication, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate(configuration);
        ArgumentNullException.ThrowIfNull(local);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(clock);
        this.configuration = configuration;
        this.options = options;
        this.local = local;
        this.authentication = authentication;
        this.clock = clock;
        endpoints = new(options.Endpoints, StringComparer.Ordinal);
        credential = options.Secret.ToArray();
        var security = new PeerSecurity(credential, clock, options.ConnectTimeout);
        http = new HttpClient(security.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>Returns a current silo generation, refreshing once under the caller's bounded deadline.</summary>
    /// <param name="voterId">The configured voter to resolve.</param>
    /// <param name="refresh">Whether to bypass a still-fresh cache entry.</param>
    /// <param name="cancellationToken">Cancellation for the wait, HTTP request and bounded response read.</param>
    /// <returns>The current Orleans silo address including its generation.</returns>
    public async Task<SiloAddress> ResolveAsync(string voterId, bool refresh, CancellationToken cancellationToken)
    {
        if (!endpoints.ContainsKey(voterId))
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, ReplicaProtocol.InvalidPeer);
        }

        if (voterId == configuration.LocalId)
        {
            return SiloAddress.FromParsableString(local.RuntimeAddress);
        }

        if (!refresh && TryCached(voterId, out var address))
        {
            return address!;
        }

        await discoveryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!refresh && TryCached(voterId, out address))
            {
                return address!;
            }

            address = await DiscoverAsync(voterId, cancellationToken).ConfigureAwait(false);
            cache[voterId] = new(address, clock.GetTimestamp());
            return address;
        }
        finally
        {
            discoveryGate.Release();
        }
    }

    private bool TryCached(string voterId, out SiloAddress? address)
    {
        address = null;
        if (!cache.TryGetValue(voterId, out var cached)
            || clock.GetElapsedTime(cached.Timestamp) >= configuration.LowerElectionTimeout)
        {
            return false;
        }

        address = cached.Address;
        return true;
    }

    private async Task<SiloAddress> DiscoverAsync(string voterId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoints[voterId], ReplicaProtocol.DiscoveryPath));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK
            || !request.Headers.TryGetValues(ReplicaTransportProtocol.HttpNonceHeader, out var nonces)
            || !Guid.TryParseExact(SingleHeader(nonces), ReplicaTransportProtocol.NonceFormat, out var nonce)
            || !response.Headers.TryGetValues(ReplicaTransportProtocol.DiscoverySignatureHeader, out var signatures))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var bytes = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
        authentication.VerifyDiscovery(voterId, bytes, nonce, SingleHeader(signatures));
        var discovered = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(bytes);
        return ValidateDiscovery(voterId, discovered);
    }

    private SiloAddress ValidateDiscovery(string voterId, ReplicaSiloDiscovery discovered)
    {
        if (discovered.VoterId != voterId || discovered.ClusterId != options.ClusterId
            || discovered.Incarnation != configuration.Incarnation || !discovered.TransportReady
            || string.IsNullOrWhiteSpace(discovered.SiloAddress)
            || discovered.SiloAddress.Length > ReplicaTransportProtocol.MaximumAddressCharacters)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var address = SiloAddress.FromParsableString(discovered.SiloAddress);
        if (address.Endpoint.Port <= 0 || address.ToParsableString() != discovered.SiloAddress)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaTransportProtocol.InvalidDiscovery);
        }

        return address;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > ReplicaTransportProtocol.MaximumDiscoveryBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var bytes = new byte[ReplicaTransportProtocol.MaximumDiscoveryBytes + 1];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.AsSpan(0, count).ToArray();
            }

            count += read;
        }

        throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        http.Dispose();
        discoveryGate.Dispose();
        CryptographicOperations.ZeroMemory(credential);
    }

    private static string SingleHeader(IEnumerable<string> values)
    {
        using var entries = values.GetEnumerator();
        if (!entries.MoveNext())
        {
            return string.Empty;
        }

        var value = entries.Current;
        return entries.MoveNext() ? string.Empty : value;
    }

    private sealed record CachedAddress(SiloAddress Address, long Timestamp);
}
