using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace KeyLoad.Replication;

public sealed class PeerSecurity(byte[] secret) : IHttpMessageHandlerFactory
{
    private readonly ConcurrentDictionary<string, long> nonces = new(StringComparer.Ordinal);
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private string Signature(string method, string authority, string path, string timestamp, string nonce, byte[] body)
        => Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"{method}\n{authority.ToLowerInvariant()}\n{path}\n{timestamp}\n{nonce}\n{Hash(body)}")));
    public HttpMessageHandler CreateHandler(string name) => CreateHandler();
    public HttpMessageHandler CreateHandler() => new SignedHandler(this) { InnerHandler = new SocketsHttpHandler
    { ConnectTimeout = TimeSpan.FromSeconds(2), PooledConnectionLifetime = TimeSpan.FromMinutes(5) } };
    private sealed class SignedHandler(PeerSecurity security) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
            var nonce = Guid.NewGuid().ToString("N");
            request.Headers.Add("X-KeyLoad-Time", timestamp); request.Headers.Add("X-KeyLoad-Nonce", nonce);
            request.Headers.Add("X-KeyLoad-Signature", security.Signature(request.Method.Method, request.RequestUri!.Authority,
                request.RequestUri.PathAndQuery, timestamp, nonce, body));
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
    public async Task<bool> ValidateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var timestamp = request.Headers["X-KeyLoad-Time"].ToString();
        var nonce = request.Headers["X-KeyLoad-Nonce"].ToString();
        var signature = request.Headers["X-KeyLoad-Signature"].ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!long.TryParse(timestamp, out var time) || time < now - 30_000 || time > now + 30_000
            || !Guid.TryParseExact(nonce, "N", out _) || signature.Length != 64)
            return false;
        if (nonces.Count > 10_000)
        {
            foreach (var pair in nonces.Where(p => p.Value < now - 60_000)) nonces.TryRemove(pair.Key, out _);
            if (nonces.Count > 10_000) return false;
        }
        request.EnableBuffering(32_768, 33_554_432);
        using var body = new MemoryStream();
        await request.Body.CopyToAsync(body, cancellationToken).ConfigureAwait(false);
        request.Body.Position = 0;
        if (!Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var recipient)) return false;
        var expected = Signature(request.Method, recipient.Authority, (request.Path.Value ?? "/") + request.QueryString.Value,
            timestamp, nonce, body.ToArray());
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature))) return false;
        return nonces.TryAdd(nonce, time);
    }
}
