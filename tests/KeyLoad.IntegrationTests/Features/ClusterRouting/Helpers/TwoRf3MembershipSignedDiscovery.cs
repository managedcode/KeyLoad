using System.Net;
using System.Security.Cryptography;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Replication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipSignedDiscovery
{
    private const string Invalid = "A signed membership observation did not match its actual voter.";
    private const int SignatureLength = 64;

    internal static async Task<ReplicaSiloDiscovery> ReadAsync(DistributedApplication app, string node,
        string clusterId, Guid incarnation, ReadOnlyMemory<byte> secret, CancellationToken token)
    {
        using var security = new PeerSecurity(secret, TimeProvider.System, IntegrationRoutingOptions.Discovery());
        using var http = McpCallerHttp.Create(app, node);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(http.BaseAddress ?? throw new InvalidOperationException(Invalid), ReplicaProtocol.DiscoveryPath));
        security.Sign(request);
        var nonce = ReadNonce(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        { throw new InvalidOperationException(Invalid); }
        var payload = await ReadPayloadAsync(response, token).ConfigureAwait(false);
        VerifyMac(response, node, clusterId, incarnation, nonce, secret, payload);
        return ReadNative(clusterId, incarnation, node, payload);
    }

    private static Guid ReadNonce(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues(ReplicaTransportProtocol.HttpNonceHeader, out var values))
        { throw new InvalidOperationException(Invalid); }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { throw new InvalidOperationException(Invalid); }
        var value = iterator.Current;
        if (iterator.MoveNext() || !Guid.TryParseExact(value, ReplicaTransportProtocol.NonceFormat, out var nonce)
            || nonce == Guid.Empty)
        { throw new InvalidOperationException(Invalid); }
        return nonce;
    }

    private static async Task<byte[]> ReadPayloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = ReplicaTransportProtocol.MaximumDiscoveryBytes;
        if (response.Content.Headers.ContentLength is { } length && length > maximum)
        { throw new InvalidDataException(Invalid); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var payload = new byte[maximum + 1];
        var count = 0;
        while (count < payload.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(payload.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0)
            { break; }
            count += read;
        }
        if (count > maximum || response.Content.Headers.ContentLength is { } expected && expected != count)
        { throw new InvalidDataException(Invalid); }
        return payload.AsSpan(0, count).ToArray();
    }

    private static void VerifyMac(HttpResponseMessage response, string node, string clusterId, Guid incarnation,
        Guid nonce, ReadOnlyMemory<byte> secret, byte[] payload)
    {
        if (!response.Headers.TryGetValues(ReplicaTransportProtocol.DiscoverySignatureHeader, out var values))
        { throw new InvalidOperationException(Invalid); }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { throw new InvalidOperationException(Invalid); }
        var text = iterator.Current;
        if (iterator.MoveNext() || text.Length != SignatureLength)
        { throw new InvalidOperationException(Invalid); }
        byte[] supplied;
        try
        { supplied = Convert.FromHexString(text); }
        catch (FormatException) { throw new InvalidOperationException(Invalid); }
        using var mac = new ReplicaMessageMac(secret, clusterId);
        var expected = mac.Discovery(incarnation, Voter(node), nonce, payload);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            { throw new InvalidOperationException(Invalid); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }

    private static ReplicaSiloDiscovery ReadNative(string clusterId, Guid incarnation, string node, byte[] payload)
    {
        var discovery = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(payload)
            ?? throw new InvalidOperationException(Invalid);
        if (discovery.VoterId != Voter(node) || discovery.ClusterId != clusterId || discovery.Incarnation != incarnation
            || !discovery.TransportReady || discovery.SiloAddress.Length is 0 or > ReplicaTransportProtocol.MaximumAddressCharacters)
        { throw new InvalidOperationException(Invalid); }
        var address = SiloAddress.FromParsableString(discovery.SiloAddress);
        if (address.Endpoint.Port <= 0 || address.ToParsableString() != discovery.SiloAddress)
        { throw new InvalidOperationException(Invalid); }
        return discovery;
    }

    private static string Voter(string node) => node switch
    {
        TwoRf3MembershipProtocol.Node1 => "http://node1:8080",
        TwoRf3MembershipProtocol.Node2 => "http://node2:8080",
        TwoRf3MembershipProtocol.Node3 => "http://node3:8080",
        TwoRf3MembershipProtocol.Node4 => "http://node4:8080",
        TwoRf3MembershipProtocol.Node5 => "http://node5:8080",
        TwoRf3MembershipProtocol.Node6 => "http://node6:8080",
        _ => throw new ArgumentOutOfRangeException(nameof(node))
    };
}
