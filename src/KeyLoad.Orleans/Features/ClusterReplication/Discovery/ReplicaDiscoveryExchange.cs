using System.Net;
using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Performs one bounded authenticated HTTP discovery without retaining response bodies.</summary>
internal sealed class ReplicaDiscoveryExchange : IDisposable
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaPeerOptions options;
    private readonly ReplicaEnvelopeAuthenticator authentication;
    private readonly TimeProvider clock;
    private readonly Dictionary<string, Uri> endpoints;
    private readonly PeerSecurity security;
    private readonly HttpClient http;

    internal ReplicaDiscoveryExchange(IOptions<ReplicaConfiguration> configurationOptions, IOptions<ReplicaPeerOptions> peerSettings,
        ReplicaEnvelopeAuthenticator authentication, TimeProvider clock, IOptions<PeerDiscoveryOptions> peerOptions)
    {
        var configuration = configurationOptions.Value;
        var options = peerSettings.Value;
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(clock);
        this.configuration = configurationOptions.Value;
        this.options = peerSettings.Value;
        this.authentication = authentication;
        this.clock = clock;
        endpoints = new(options.Endpoints, StringComparer.Ordinal);
        security = new PeerSecurity(options.Secret, clock, peerOptions);
        http = new HttpClient(security.CreateHandler(), disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<ReplicaDiscoveryObservation?> DiscoverAsync(string voterId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(endpoints[voterId], ReplicaProtocol.DiscoveryPath));
        var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response is null)
        {
            return null;
        }

        using (response)
        {
            if (!TryReadAuthentication(request, response, out var nonce, out var signature))
            {
                return null;
            }

            var bytes = await ReadPayloadAsync(response, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                return null;
            }

            authentication.VerifyDiscovery(voterId, bytes, nonce, signature);
            var discovered = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(bytes);
            return ReplicaDiscoveryIdentity.CreateObservation(voterId, discovered, configuration, options, clock);
        }
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (KeyLoadException error) when (IsUnavailableTransport(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static bool IsUnavailableTransport(KeyLoadException error)
        => error.Code == ErrorCode.OwnershipLost
            && error.Message == PeerDiscoveryProtocol.Unavailable;

    private static bool TryReadAuthentication(HttpRequestMessage request, HttpResponseMessage response,
        out Guid nonce, out string signature)
    {
        nonce = Guid.Empty;
        signature = string.Empty;
        if (response.StatusCode != HttpStatusCode.OK
            || !request.Headers.TryGetValues(ReplicaTransportProtocol.HttpNonceHeader, out var nonces)
            || !Guid.TryParseExact(SingleHeader(nonces), ReplicaTransportProtocol.NonceFormat, out nonce)
            || !response.Headers.TryGetValues(ReplicaTransportProtocol.DiscoverySignatureHeader, out var signatures))
        {
            return false;
        }

        signature = SingleHeader(signatures);
        return true;
    }

    private static async Task<byte[]?> ReadPayloadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > ReplicaTransportProtocol.MaximumDiscoveryBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
        }

        var bytes = new byte[ReplicaTransportProtocol.MaximumDiscoveryBytes + 1];
        await using var ownedStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await ownedStream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.AsSpan(0, count).ToArray();
            }

            count += read;
        }

        throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaTransportProtocol.InvalidDiscovery);
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

    public void Dispose()
    {
        try
        {
            http.Dispose();
        }
        finally
        {
            security.Dispose();
        }
    }
}
