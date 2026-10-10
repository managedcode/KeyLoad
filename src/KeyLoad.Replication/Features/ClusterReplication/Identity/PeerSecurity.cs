using System.Globalization;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

/// <summary>Authenticates only bounded, bodyless silo discovery; replica commands use native signed Orleans envelopes.</summary>
public sealed class PeerSecurity : IDisposable
{
    private const int DisposalUnclaimed = 0;
    private const int DisposalClaimed = 1;

    private readonly byte[] secret;
    private readonly TimeProvider clock;
    private readonly TimeSpan connectTimeout;
    private readonly TimeSpan pooledConnectionLifetime;
    private readonly long timestampWindowMilliseconds;
    private readonly PeerDiscoveryReplay replay;
    private int disposed;

    /// <summary>Copies the configured peer credential once and fixes the clock and bounded replay capacity.</summary>
    /// <param name="secret">The shared 32-byte peer credential.</param>
    /// <param name="clock">The production system clock.</param>
    /// <param name="options">Centrally validated discovery settings, frozen for this authenticated transport owner.</param>
    public PeerSecurity(ReadOnlyMemory<byte> secret, TimeProvider clock, IOptions<PeerDiscoveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        if (secret.Length != PeerDiscoveryProtocol.SecretBytes)
        { throw Errors.Fail(ErrorCode.Validation, PeerDiscoveryProtocol.InvalidConfiguration); }
        var settings = options.Value;
        settings.Validate();
        this.secret = secret.ToArray();
        this.clock = clock;
        connectTimeout = settings.ConnectTimeout;
        pooledConnectionLifetime = settings.PooledConnectionLifetime;
        timestampWindowMilliseconds = settings.TimestampWindow.Ticks / TimeSpan.TicksPerMillisecond;
        replay = new(settings.ReplayCapacity, timestampWindowMilliseconds);
    }

    /// <summary>Signs the exact recipient, path and fresh nonce of a permitted bodyless discovery GET.</summary>
    /// <param name="request">The actual outbound discovery request.</param>
    /// <exception cref="KeyLoadException">The request is outside the discovery-only contract.</exception>
    public void Sign(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        if (!PeerDiscoveryRequest.Valid(request))
        { throw Errors.Fail(ErrorCode.Validation, PeerDiscoveryProtocol.InvalidRequest); }
        var timestamp = clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString(PeerDiscoveryProtocol.NonceFormat);
        var uri = request.RequestUri!;
        var signature = Convert.ToHexStringLower(PeerDiscoverySignature.Compute(secret, request.Method.Method,
            uri.Authority, uri.AbsolutePath, timestamp, nonce));
        Set(request, PeerDiscoveryProtocol.TimeHeader, timestamp);
        Set(request, PeerDiscoveryProtocol.NonceHeader, nonce);
        Set(request, PeerDiscoveryProtocol.SignatureHeader, signature);
    }

    /// <summary>Creates the real bounded socket handler; it signs only permitted discovery requests.</summary>
    /// <returns>An independently owned HTTP handler with automatic redirects disabled.</returns>
    /// <remarks>The handler borrows this signer, which must outlive all of its active requests.</remarks>
    public HttpMessageHandler CreateHandler()
    {
        ThrowIfDisposed();
        return new SignedHandler(this)
        {
            InnerHandler = new SocketsHttpHandler
            { ConnectTimeout = connectTimeout, PooledConnectionLifetime = pooledConnectionLifetime, AllowAutoRedirect = false }
        };
    }

    // Protected fixture enrollment only. The ordinary handler path is unchanged.
    internal HttpMessageHandler CreateProtectedHandler(Action<HttpRequestMessage, CancellationToken> observed)
        => ProtectedDiscoveryHandlerCreation.Create(this, observed);

    /// <summary>Validates the exact request and an empty body before consuming bounded nonce admission.</summary>
    /// <param name="request">The actual receiver request.</param>
    /// <param name="cancellationToken">Cancellation of discovery validation.</param>
    /// <returns>False for malformed, forged, expired or replayed requests.</returns>
    /// <exception cref="KeyLoadException">An authenticated request reaches the fixed replay capacity.</exception>
    public async Task<bool> ValidateAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!PeerDiscoveryRequest.Valid(request)
            || !PeerDiscoverySignature.Read(request, out var timestamp, out var nonce, out var nonceText, out var signature))
        { return false; }
        var now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        if (!PeerDiscoverySignature.Fresh(timestamp, now, timestampWindowMilliseconds, out _))
        { return false; }
        var expected = PeerDiscoverySignature.Compute(secret, request.Method, request.Host.Value!,
            request.Path.Value!, timestamp, nonceText);
        if (!CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(signature))
            || !await PeerDiscoveryRequest.EmptyBodyAsync(request, cancellationToken))
        { return false; }
        now = clock.GetUtcNow().ToUnixTimeMilliseconds();
        return PeerDiscoverySignature.Fresh(timestamp, now, timestampWindowMilliseconds, out var time) && replay.Admit(nonce, time, now);
    }

    private static void Set(HttpRequestMessage request, string header, string value)
    {
        request.Headers.Remove(header);
        request.Headers.Add(header, value);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != DisposalUnclaimed, this);

    /// <summary>Clears the owned credential after the owner has joined every active borrower.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, DisposalClaimed) == DisposalUnclaimed)
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    private sealed class SignedHandler(PeerSecurity security) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            security.Sign(request);
            try
            { return await base.SendAsync(request, cancellationToken); }
            catch (Exception error) when (error is HttpRequestException or IOException
                || error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
            { throw Errors.Fail(ErrorCode.OwnershipLost, PeerDiscoveryProtocol.Unavailable); }
        }
    }
}
