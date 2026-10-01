using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace KeyLoad.Replication;

public sealed class PeerSecurity(byte[] secret, long maxBodyBytes = 4_294_967_296, TimeSpan? connectTimeout = null, ILogger<PeerSecurity>? logger = null) : IHttpMessageHandlerFactory
{
    private readonly ConcurrentDictionary<string, long> nonces = new(StringComparer.Ordinal);
    private readonly ILogger<PeerSecurity>? diagnostics = logger;
    public long MaxBodyBytes { get; } = maxBodyBytes;
    private string Signature(string method, string authority, string path, string timestamp, string nonce, string bodyHash, string protocolHash)
        => Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"keyload-peer-v2\n{method}\n{authority.ToLowerInvariant()}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}\n{protocolHash}")));
    private static string ProtocolHash(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers, string? contentType)
    {
        var fields = headers.Where(header => header.Key.StartsWith("X-Raft-", StringComparison.OrdinalIgnoreCase)
            || header.Key.Equals("X-Request-ID", StringComparison.OrdinalIgnoreCase)).OrderBy(header => header.Key, StringComparer.OrdinalIgnoreCase)
            .Select(header => header.Key.ToLowerInvariant() + "\n" + string.Join(",", header.Value));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", fields) + "\ncontent-type\n" + contentType)));
    }
    public HttpMessageHandler CreateHandler(string name) => CreateHandler();
    public HttpMessageHandler CreateHandler() => new SignedHandler(this) { InnerHandler = new SocketsHttpHandler
    { ConnectTimeout = connectTimeout ?? TimeSpan.FromMilliseconds(500), PooledConnectionLifetime = TimeSpan.FromMinutes(5), AllowAutoRedirect = false } };
    private sealed class SignedHandler(PeerSecurity security) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var original = request.Content;
            FileStream? staged = null; StreamContent? content = null;
            try
            {
                var hash = Convert.ToHexStringLower(SHA256.HashData(ReadOnlySpan<byte>.Empty));
                if (original is not null)
                {
                    var fileOptions = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.ReadWrite,
                        Share = FileShare.None, BufferSize = 65_536, Options = FileOptions.Asynchronous | FileOptions.DeleteOnClose | FileOptions.SequentialScan };
                    if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                    staged = new FileStream(Path.Combine(Path.GetTempPath(), "keyload-peer-" + Guid.NewGuid().ToString("N")), fileOptions);
                    // Serialize a potentially one-shot provider payload exactly once. The bounded spool keeps large snapshots out of RAM.
                    await original.CopyToAsync(new BoundedWriter(staged, security.MaxBodyBytes), cancellationToken).ConfigureAwait(false);
                    await staged.FlushAsync(cancellationToken).ConfigureAwait(false); staged.Position = 0;
                    hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(staged, cancellationToken).ConfigureAwait(false));
                    staged.Position = 0; content = new StreamContent(staged, 65_536);
                    foreach (var header in original.Headers) content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    request.Content = content;
                }
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
                var nonce = Guid.NewGuid().ToString("N");
                foreach (var name in new[] { "X-KeyLoad-Time", "X-KeyLoad-Nonce", "X-KeyLoad-Body-Hash", "X-KeyLoad-Signature" }) request.Headers.Remove(name);
                request.Headers.Add("X-KeyLoad-Time", timestamp); request.Headers.Add("X-KeyLoad-Nonce", nonce);
                request.Headers.Add("X-KeyLoad-Body-Hash", hash);
                request.Headers.Add("X-KeyLoad-Signature", security.Signature(request.Method.Method, request.RequestUri!.Authority,
                    request.RequestUri.PathAndQuery, timestamp, nonce, hash, ProtocolHash(request.Headers, request.Content?.Headers.ContentType?.ToString())));
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (request.Headers.TryGetValues("X-Raft-Message-Type", out var types) && types.SingleOrDefault() == "InstallSnapshot")
                {
                    security.diagnostics?.LogInformation("Snapshot response from {Recipient}: HTTP {Status}, receiver term {Term}",
                        request.RequestUri.Authority, (int)response.StatusCode,
                        response.Headers.TryGetValues("X-Raft-Term", out var terms) ? terms.SingleOrDefault() : "missing");
                }
                return response;
            }
            finally { request.Content = original; content?.Dispose(); if (staged is not null) await staged.DisposeAsync().ConfigureAwait(false); }
        }
    }
    private sealed class BoundedWriter(Stream target, long limit) : Stream
    {
        private long written;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }
        private void Reserve(int count)
        {
            if (count > limit - written) throw Errors.Fail(ErrorCode.ResourceExhausted, "The peer payload exceeds its transport byte budget.");
            written += count;
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { Reserve(buffer.Length); target.Write(buffer); }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        { Reserve(buffer.Length); return target.WriteAsync(buffer, cancellationToken); }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => target.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => target.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
    private sealed class VerifiedBodyPipe(Stream body) : Microsoft.AspNetCore.Http.Features.IRequestBodyPipeFeature, IAsyncDisposable
    {
        public PipeReader Reader { get; } = PipeReader.Create(body, new StreamPipeReaderOptions(leaveOpen: true));
        public ValueTask DisposeAsync() => Reader.CompleteAsync();
    }
    public async Task<bool> ValidateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var timestamp = request.Headers["X-KeyLoad-Time"].ToString();
        var nonce = request.Headers["X-KeyLoad-Nonce"].ToString();
        var signature = request.Headers["X-KeyLoad-Signature"].ToString();
        var bodyHash = request.Headers["X-KeyLoad-Body-Hash"].ToString();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!long.TryParse(timestamp, out var time) || time < now - 30_000 || time > now + 30_000
            || !Guid.TryParseExact(nonce, "N", out _) || signature.Length != 64 || bodyHash.Length != 64 || !bodyHash.All(Uri.IsHexDigit)) return false;
        if (nonces.Count > 10_000)
        {
            foreach (var pair in nonces.Where(p => p.Value < now - 60_000)) nonces.TryRemove(pair.Key, out _);
            if (nonces.Count > 10_000) return false;
        }
        if (!Uri.TryCreate($"{request.Scheme}://{request.Host}", UriKind.Absolute, out var recipient)) return false;
        var protocol = request.Headers.Select(header => new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.Select(value => value ?? "")));
        var expected = Signature(request.Method, recipient.Authority, (request.Path.Value ?? "/") + request.QueryString.Value, timestamp, nonce, bodyHash,
            ProtocolHash(protocol, request.ContentType));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature)) || !nonces.TryAdd(nonce, time)) return false;
        // Authenticate headers before accepting a disk-backed, bounded body. The payload hash is checked before dispatch.
        request.EnableBuffering(65_536, MaxBodyBytes);
        var actual = await SHA256.HashDataAsync(request.Body, cancellationToken).ConfigureAwait(false);
        request.Body.Position = 0;
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(bodyHash))) return false;
        // Kestrel's original pipe has already been consumed by verification. Recreate BodyReader over the verified, rewound spool.
        var verifiedPipe = new VerifiedBodyPipe(request.Body);
        request.HttpContext.Features.Set<Microsoft.AspNetCore.Http.Features.IRequestBodyPipeFeature>(verifiedPipe);
        request.HttpContext.Response.RegisterForDisposeAsync(verifiedPipe);
        return true;
    }
}
