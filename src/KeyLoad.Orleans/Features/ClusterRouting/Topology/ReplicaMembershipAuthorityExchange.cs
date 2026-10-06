using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipAuthorityExchange : IDisposable
{
    private readonly ReplicaMembershipAuthorityExchangeOptions options;
    private readonly ReplicaMembershipAuthorityMac requestMac;
    private readonly ReplicaMembershipAuthorityMac replyMac;
    private readonly HttpClient http;
    private readonly IOptions<OrleansMembershipOptions> membershipOptions;

    internal ReplicaMembershipAuthorityExchange(ReplicaMembershipAuthorityExchangeOptions options,
        IOptions<OrleansMembershipOptions> membershipOptions)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        ArgumentNullException.ThrowIfNull(membershipOptions);
        membershipOptions.Value.Validate();
        this.membershipOptions = membershipOptions;
        this.options = options;
        requestMac = new(options.CallerPeerSecret, membershipOptions);
        replyMac = new(options.AuthorityPeerSecret, membershipOptions);
        SocketsHttpHandler? handler = new()
        {
            AllowAutoRedirect = false,
            ConnectTimeout = membershipOptions.Value.ConnectTimeout,
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
        var body = ReplicaMembershipAuthorityCodec.SerializeCall(call: call, membershipOptions: membershipOptions);
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
        const char TrimCharCharacter = '=';
        const char OldCharCharacter = '+';
        const char NewCharCharacter = '-';
        const char SlashCharacter = '/';
        const char UnderscoreCharacter = '_';

        var timestamp = options.Clock.GetUtcNow().UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var nonceBytes = RandomNumberGenerator.GetBytes(ReplicaMembershipAuthorityProtocol.NonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes).TrimEnd(TrimCharCharacter).Replace(OldCharCharacter, NewCharCharacter).Replace(SlashCharacter, UnderscoreCharacter);
        CryptographicOperations.ZeroMemory(nonceBytes);
        var cluster = options.ClusterId;
        var authorityPhysical = options.AuthorityPhysicalShardId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat);
        var authorityIncarnation = options.AuthorityIncarnation.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat);
        var callerPhysical = options.CallerPhysicalShardId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat);
        var callerIncarnation = options.CallerIncarnation.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat);
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
                call.RequestId.ToString(ReplicaMembershipAuthorityProtocol.IdentityFormat), nonce, (int)response.StatusCode, responseBytes, replySignature))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ReplicaMembershipAuthorityText.InvalidSignature); }
        var reply = ReplicaMembershipAuthorityCodec.DeserializeReply(bytes: responseBytes, membershipOptions: membershipOptions);
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

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        const int MaximumReplyBytesStep = 1;
        const int CountInitialValue = 0;
        const int EmptyRead = 0;
        const int StartEmptyCount = 0;

        if (response.Content.Headers.ContentLength > membershipOptions.Value.MaximumReplyBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaMembershipAuthorityText.ReplyTooLarge); }
        var buffer = new byte[membershipOptions.Value.MaximumReplyBytes + MaximumReplyBytesStep];
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var count = CountInitialValue;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == EmptyRead)
            { return buffer.AsSpan(StartEmptyCount, count).ToArray(); }
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
        const int EmptyAuthorityEndpointsCount = 3;

        if (string.IsNullOrWhiteSpace(options.ClusterId) || options.AuthorityPhysicalShardId == Guid.Empty
            || options.AuthorityIncarnation == Guid.Empty || options.CallerPhysicalShardId == Guid.Empty
            || options.CallerIncarnation == Guid.Empty || string.IsNullOrWhiteSpace(options.CallerVoterId)
            || string.IsNullOrWhiteSpace(options.CallerSiloAddress) || options.AuthorityEndpoints is null
            || options.AuthorityEndpoints.Count != EmptyAuthorityEndpointsCount || options.AuthorityEndpoints.Any(endpoint => endpoint is null)
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
