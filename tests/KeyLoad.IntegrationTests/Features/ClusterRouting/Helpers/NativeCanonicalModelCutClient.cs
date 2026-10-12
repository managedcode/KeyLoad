using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutClient
{
    private const string Invalid = "The actual local administrator cut response failed its original native identity or signature.";
    private const string BearerScheme = "Bearer";
    private const int SignatureCharacters = 64;
    private const int OverflowByte = 1;

    internal static async Task<IReadOnlyDictionary<string, NativeCanonicalModelCutWitness>> ReadSixAsync(
        TwoRf3MembershipWave wave, string adminCredential, NativeCanonicalModelCutRequest originalScope,
        IReadOnlyDictionary<string, ReplicaSiloDiscovery> retainedSignedOwners,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> configuredPeerSecrets, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(wave);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminCredential);
        NativeCanonicalModelCutCodec.ValidateRequest(originalScope);
        RequireInventory(retainedSignedOwners, configuredPeerSecrets);
        if (originalScope.SessionId != wave.QueryControls.SessionId)
        { throw new InvalidDataException(Invalid); }
        var controls = wave.canonicalCutControls ?? throw new InvalidDataException(Invalid);
        controls.MirrorArm(originalScope.ArmId);
        var cuts = new Dictionary<string, NativeCanonicalModelCutWitness>(StringComparer.Ordinal);
        var nonces = new HashSet<Guid>();
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            token.ThrowIfCancellationRequested();
            var request = originalScope with { Nonce = Guid.NewGuid() };
            if (!nonces.Add(request.Nonce))
            { throw new InvalidDataException(Invalid); }
            cuts.Add(node, await ReadAsync(wave.Application, node, adminCredential, request, retainedSignedOwners[node],
                configuredPeerSecrets[node], token).ConfigureAwait(false));
        }
        return cuts;
    }

    private static void RequireInventory(IReadOnlyDictionary<string, ReplicaSiloDiscovery> owners,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> secrets)
    {
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(secrets);
        if (owners.Count != TwoRf3MembershipProtocol.NodeCount || secrets.Count != TwoRf3MembershipProtocol.NodeCount)
        { throw new InvalidDataException(Invalid); }
        var voters = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            if (!owners.TryGetValue(node, out var owner) || owner is null || !owner.TransportReady
                || !voters.Add(owner.VoterId) || !addresses.Add(owner.SiloAddress)
                || !secrets.TryGetValue(node, out var secret) || secret.Length != ReplicaTransportProtocol.SecretBytes)
            { throw new InvalidDataException(Invalid); }
        }
    }

    private static async Task<NativeCanonicalModelCutWitness> ReadAsync(Aspire.Hosting.DistributedApplication app,
        string node, string credential, NativeCanonicalModelCutRequest original, ReplicaSiloDiscovery owner,
        ReadOnlyMemory<byte> secret, CancellationToken token)
    {
        var failures = new List<Exception>();
        HttpClient? http = null;
        NativeCanonicalModelCutWitness? witness = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            http = McpCallerHttp.Create(app, node);
            using var request = new HttpRequestMessage(HttpMethod.Post, NativeCanonicalModelCutDiagnosticProtocol.Path);
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, credential);
            request.Content = new ByteArrayContent(NativeSerialization.Serialize(original));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NativeCanonicalModelCutDiagnosticProtocol.ContentType);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK
                || response.Content.Headers.ContentType?.MediaType != NativeCanonicalModelCutDiagnosticProtocol.ContentType)
            { throw new InvalidDataException(Invalid); }
            var payload = await PayloadAsync(response, token).ConfigureAwait(false);
            Verify(response, owner, original.Nonce, secret, payload);
            witness = NativeCanonicalModelCutCodec.Decode(payload, token);
            NativeCanonicalModelCutAssertions.Require(original, owner, witness, token);
        }, failures).ConfigureAwait(false);
        if (http is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
        return witness ?? throw new InvalidDataException(Invalid);
    }

    private static async Task<byte[]> PayloadAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = NativeCanonicalModelCutCodec.MaximumRecordBytes;
        if (response.Content.Headers.ContentLength is { } length && length > maximum)
        { throw new InvalidDataException(Invalid); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var payload = new byte[maximum + OverflowByte];
        var count = 0;
        while (count < payload.Length)
        {
            var read = await stream.ReadAsync(payload.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0)
            { break; }
            count += read;
        }
        if (count > maximum || response.Content.Headers.ContentLength is { } expected && expected != count)
        { throw new InvalidDataException(Invalid); }
        return payload.AsSpan(0, count).ToArray();
    }

    private static void Verify(HttpResponseMessage response, ReplicaSiloDiscovery owner, Guid nonce,
        ReadOnlyMemory<byte> secret, byte[] payload)
    {
        if (!response.Headers.TryGetValues(NativeCanonicalModelCutDiagnosticProtocol.SignatureHeader, out var values))
        { throw new InvalidDataException(Invalid); }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { throw new InvalidDataException(Invalid); }
        var signature = iterator.Current;
        if (iterator.MoveNext() || signature.Length != SignatureCharacters)
        { throw new InvalidDataException(Invalid); }
        byte[] supplied;
        try
        { supplied = Convert.FromHexString(signature); }
        catch (FormatException) { throw new InvalidDataException(Invalid); }
        using var mac = new ReplicaMessageMac(secret, owner.ClusterId);
        var expected = mac.Discovery(owner.Incarnation, owner.VoterId, nonce,
            NativeCanonicalModelCutSignatureFrame.Create(payload));
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(expected, supplied))
            { throw new InvalidDataException(Invalid); }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(supplied);
        }
    }
}
