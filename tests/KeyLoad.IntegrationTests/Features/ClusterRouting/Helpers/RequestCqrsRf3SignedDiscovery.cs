using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Storage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3SignedDiscovery
{
    private const string ClusterPrefix = "keyload-";
    private const string DiscoveryPath = ReplicaProtocol.DiscoveryPath;
    private const string InvalidSignedObservation = "The actual signed RF3 discovery observation is invalid.";
    private const string BodyTooLarge = "The signed RF3 discovery observation exceeded its native byte bound.";
    private const int SignatureCharacters = 64;

    internal static async Task<ReplicaSiloDiscovery> ReadAsync(DistributedApplication app, string nodeName,
        Guid incarnation, ReadOnlyMemory<byte> peerSecret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        if (peerSecret.Length != ReplicaTransportProtocol.SecretBytes)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        return await ReadOwnedAsync(app, nodeName, incarnation, peerSecret, cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<ReplicaSiloDiscovery> ReadForProfileAsync(DistributedApplication app,
        string nodeName, NodeEpochRf3Profile profile, CancellationToken cancellationToken)
    {
        var credential = Convert.FromBase64String(profile.PeerSecret);
        try
        { return await ReadAsync(app, nodeName, profile.Incarnation, credential, cancellationToken).ConfigureAwait(false); }
        finally
        { CryptographicOperations.ZeroMemory(credential); }
    }

    private static async Task<ReplicaSiloDiscovery> ReadOwnedAsync(DistributedApplication app, string nodeName,
        Guid incarnation, ReadOnlyMemory<byte> credential, CancellationToken cancellationToken)
    {
        using var signer = new PeerSecurity(credential, TimeProvider.System, IntegrationRoutingOptions.Discovery());
        using var http = McpCallerHttp.Create(app, nodeName);
        using var request = new HttpRequestMessage(HttpMethod.Get,
            new Uri(http.BaseAddress ?? throw new InvalidOperationException(InvalidSignedObservation), DiscoveryPath));
        signer.Sign(request);
        var nonce = ReadNonce(request);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        var payload = await ReadBoundedPayloadAsync(response, cancellationToken).ConfigureAwait(false);
        VerifySignature(response, incarnation, nodeName, nonce, credential, payload);
        return ValidateNativeRecord(incarnation, nodeName, payload);
    }

    private static Guid ReadNonce(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues(ReplicaTransportProtocol.HttpNonceHeader, out var values))
        { throw new InvalidOperationException(InvalidSignedObservation); }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { throw new InvalidOperationException(InvalidSignedObservation); }
        var text = iterator.Current;
        if (iterator.MoveNext()
            || !Guid.TryParseExact(text, ReplicaTransportProtocol.NonceFormat, out var nonce)
            || nonce == Guid.Empty)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        return nonce;
    }

    private static async Task<byte[]> ReadBoundedPayloadAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var maximum = ReplicaTransportProtocol.MaximumDiscoveryBytes;
        if (response.Content.Headers.ContentLength is { } length && length > maximum)
        { throw new InvalidDataException(BodyTooLarge); }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var bytes = new byte[maximum + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            { break; }
            count += read;
        }
        if (count > maximum || response.Content.Headers.ContentLength is { } expected && expected != count)
        { throw new InvalidDataException(BodyTooLarge); }
        return bytes.AsSpan(0, count).ToArray();
    }

    private static void VerifySignature(HttpResponseMessage response, Guid incarnation, string nodeName,
        Guid nonce, ReadOnlyMemory<byte> credential, byte[] payload)
    {
        if (!response.Headers.TryGetValues(ReplicaTransportProtocol.DiscoverySignatureHeader, out var values))
        { throw new InvalidOperationException(InvalidSignedObservation); }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { throw new InvalidOperationException(InvalidSignedObservation); }
        var text = iterator.Current;
        if (iterator.MoveNext() || text.Length != SignatureCharacters)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        byte[] supplied;
        try
        { supplied = Convert.FromHexString(text); }
        catch (FormatException)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        using var mac = new ReplicaMessageMac(credential, ClusterPrefix + incarnation.ToString("N", CultureInfo.InvariantCulture));
        var expected = mac.Discovery(incarnation, VoterOrigin(nodeName), nonce, payload);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            { throw new InvalidOperationException(InvalidSignedObservation); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }

    private static ReplicaSiloDiscovery ValidateNativeRecord(Guid incarnation, string nodeName, byte[] payload)
    {
        var value = NativeSerialization.Deserialize<ReplicaSiloDiscovery>(payload)
            ?? throw new InvalidOperationException(InvalidSignedObservation);
        var voter = VoterOrigin(nodeName);
        if (value.VoterId != voter || value.ClusterId != ClusterPrefix + incarnation.ToString("N", CultureInfo.InvariantCulture)
            || value.Incarnation != incarnation || !value.TransportReady
            || value.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal
            || string.IsNullOrEmpty(value.SiloAddress)
            || value.SiloAddress.Length > ReplicaTransportProtocol.MaximumAddressCharacters)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        SiloAddress address;
        try
        { address = SiloAddress.FromParsableString(value.SiloAddress); }
        catch (Exception error) when (error is FormatException or ArgumentException)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        if (address.Endpoint.Port <= 0 || address.ToParsableString() != value.SiloAddress)
        { throw new InvalidOperationException(InvalidSignedObservation); }
        return value;
    }

    private static string VoterOrigin(string nodeName) => nodeName switch
    {
        RequestCqrsRf3Protocol.Node1 => "http://node1:8080",
        RequestCqrsRf3Protocol.Node2 => "http://node2:8080",
        RequestCqrsRf3Protocol.Node3 => "http://node3:8080",
        _ => throw new ArgumentOutOfRangeException(nameof(nodeName))
    };
}
