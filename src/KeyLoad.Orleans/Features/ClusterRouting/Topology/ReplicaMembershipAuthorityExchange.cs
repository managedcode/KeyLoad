using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipAuthorityExchange : IDisposable
{
    private readonly ReplicaMembershipAuthorityExchangeOptions options;
    private readonly ReplicaMembershipAuthorityMac requestMac;
    private readonly ReplicaMembershipAuthorityMac replyMac;
    private readonly HttpClient http;

    internal ReplicaMembershipAuthorityExchange(ReplicaMembershipAuthorityExchangeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        this.options = options;
        requestMac = new(options.CallerPeerSecret);
        replyMac = new(options.AuthorityPeerSecret);
        SocketsHttpHandler? handler = new()
        {
            AllowAutoRedirect = false,
            ConnectTimeout = ReplicaTransportProtocol.DefaultConnectTimeout,
            UseCookies = false
        };
        try
        {
            http = new(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
            handler = null;
        }
        finally
        {
            handler?.Dispose();
        }
    }

    internal async Task<ReplicaMembershipAuthorityReplyV1> SendAsync(
        ReplicaMembershipAuthorityCallV1 call, CancellationToken cancellationToken)
    {
        var body = ReplicaMembershipAuthorityCodec.SerializeCall(call);
        foreach (var endpoint in options.AuthorityEndpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await SendOneAsync(endpoint, call, body, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException) { }
            catch (IOException) { }
            catch (TimeoutException) { }
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaMembershipAuthorityText.Unavailable);
    }

    private async Task<ReplicaMembershipAuthorityReplyV1> SendOneAsync(Uri endpoint,
        ReplicaMembershipAuthorityCallV1 call, byte[] body, CancellationToken cancellationToken)
    {
        var timestamp = options.Clock.GetUtcNow().UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonceBytes = RandomNumberGenerator.GetBytes(ReplicaMembershipAuthorityProtocol.NonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        CryptographicOperations.ZeroMemory(nonceBytes);
        var cluster = options.ClusterId;
        var authorityPhysical = options.AuthorityPhysicalShardId.ToString("N");
        var authorityIncarnation = options.AuthorityIncarnation.ToString("N");
        var callerPhysical = options.CallerPhysicalShardId.ToString("N");
        var callerIncarnation = options.CallerIncarnation.ToString("N");
        var signature = requestMac.SignRequest(cluster, authorityPhysical, authorityIncarnation,
            callerPhysical, callerIncarnation, options.CallerVoterId, options.CallerSiloAddress,
            timestamp, nonce, body);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, ReplicaMembershipAuthorityProtocol.Path))
        { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(ReplicaMembershipAuthorityProtocol.ContentType);
        AddRequestHeaders(request, cluster, authorityPhysical, authorityIncarnation, callerPhysical,
            callerIncarnation, options.CallerVoterId, options.CallerSiloAddress, timestamp, nonce, signature);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var replySignature = ReadSingle(response.Headers, ReplicaMembershipAuthorityProtocol.SignatureHeader);
        if (response.StatusCode == HttpStatusCode.ServiceUnavailable && replySignature is null)
        { throw new HttpRequestException(); }
        var responseBytes = await ReadBoundedAsync(response, cancellationToken).ConfigureAwait(false);
        if (replySignature is null || !replyMac.VerifyReply(authorityPhysical, authorityIncarnation,
                call.RequestId.ToString("N"), nonce, (int)response.StatusCode, responseBytes, replySignature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidSignature); }
        var reply = ReplicaMembershipAuthorityCodec.DeserializeReply(responseBytes);
        ValidateReply(call, nonce, reply);
        if ((ReplicaMembershipAuthorityResultKind)reply.ResultKind == ReplicaMembershipAuthorityResultKind.Failed)
        { throw Failure(reply); }
        if (response.StatusCode != HttpStatusCode.OK)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaMembershipAuthorityText.InvalidReply); }
        return reply;
    }

    private static void AddRequestHeaders(HttpRequestMessage request, string cluster, string authorityPhysical,
        string authorityIncarnation, string callerPhysical, string callerIncarnation, string callerVoter,
        string callerSilo, string timestamp, string nonce, string signature)
    {
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.ClusterHeader, cluster);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.AuthorityPhysicalHeader, authorityPhysical);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.AuthorityIncarnationHeader, authorityIncarnation);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.CallerPhysicalHeader, callerPhysical);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.CallerIncarnationHeader, callerIncarnation);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.CallerVoterHeader, callerVoter);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.CallerSiloHeader, callerSilo);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.TimestampHeader, timestamp);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.NonceHeader, nonce);
        request.Headers.Add(ReplicaMembershipAuthorityProtocol.SignatureHeader, signature);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > ReplicaMembershipAuthorityProtocol.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.ReplyTooLarge); }
        var buffer = new byte[ReplicaMembershipAuthorityProtocol.MaximumReplyBytes + 1];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            { return buffer.AsSpan(0, count).ToArray(); }
            count += read;
        }
        throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.ReplyTooLarge);
    }

    private static string? ReadSingle(HttpResponseHeaders headers, string name)
    {
        if (!headers.TryGetValues(name, out var values))
        { return null; }
        using var iterator = values.GetEnumerator();
        if (!iterator.MoveNext())
        { return null; }
        var value = iterator.Current;
        return iterator.MoveNext() ? null : value;
    }

    private static void ValidateReply(ReplicaMembershipAuthorityCallV1 call, string nonce,
        ReplicaMembershipAuthorityReplyV1 reply)
    {
        if (reply.AuthorityPhysicalShardId != call.AuthorityPhysicalShardId
            || reply.AuthorityIncarnation != call.AuthorityIncarnation || reply.RequestId != call.RequestId
            || !string.Equals(reply.RequestNonce, nonce, StringComparison.Ordinal))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidReply); }
    }

    private static KeyLoadException Failure(ReplicaMembershipAuthorityReplyV1 reply)
    {
        var code = reply.ErrorCode ?? ErrorCode.Corruption;
        return Errors.Fail(code, ReplicaMembershipAuthorityText.For(code));
    }

    private static void ValidateOptions(ReplicaMembershipAuthorityExchangeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClusterId) || options.AuthorityPhysicalShardId == Guid.Empty
            || options.AuthorityIncarnation == Guid.Empty || options.CallerPhysicalShardId == Guid.Empty
            || options.CallerIncarnation == Guid.Empty || string.IsNullOrWhiteSpace(options.CallerVoterId)
            || string.IsNullOrWhiteSpace(options.CallerSiloAddress) || options.AuthorityEndpoints is null
            || options.AuthorityEndpoints.Count != 3 || options.AuthorityEndpoints.Any(endpoint => endpoint is null)
            || options.CallerPeerSecret.Length != ReplicaTransportProtocol.SecretBytes
            || options.AuthorityPeerSecret.Length != ReplicaTransportProtocol.SecretBytes || options.Clock is null)
        { throw new ArgumentException(ReplicaMembershipAuthorityText.InvalidOptions); }
    }

    public void Dispose()
    {
        try
        { http.Dispose(); }
        finally
        {
            try
            { requestMac.Dispose(); }
            finally { replyMac.Dispose(); }
        }
    }
}
