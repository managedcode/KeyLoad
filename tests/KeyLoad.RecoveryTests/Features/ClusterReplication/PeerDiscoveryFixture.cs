using System.Globalization;
using System.Security.Cryptography;
using KeyLoad.Replication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace KeyLoad.RecoveryTests;

/// <summary>Mutations of genuine discovery HTTP requests, without transport doubles.</summary>
internal enum PeerDiscoveryMutation
{
    /// <summary>Changes authenticated timestamp bytes.</summary>
    Time,
    /// <summary>Changes authenticated nonce bytes.</summary>
    Nonce,
    /// <summary>Changes the HMAC.</summary>
    Signature,
    /// <summary>Changes the GET method.</summary>
    Method,
    /// <summary>Changes the exact discovery path.</summary>
    Path,
    /// <summary>Adds a path base.</summary>
    PathBase,
    /// <summary>Changes the raw target while preserving decoded path.</summary>
    RawPath,
    /// <summary>Adds a query.</summary>
    Query,
    /// <summary>Changes recipient authority.</summary>
    Recipient,
    /// <summary>Adds content type.</summary>
    ContentType,
    /// <summary>Adds a framed byte.</summary>
    FramedBody,
    /// <summary>Adds an actual byte without a length or transfer header.</summary>
    UnframedBody,
    /// <summary>Adds transfer framing.</summary>
    TransferEncoding
}

internal sealed class PeerDiscoveryFixture : IDisposable
{
    internal const string Origin = "https://node-a.test:8080";
    internal const string OtherHost = "node-b.test:8080";
    internal const string OtherPath = "/internal/commands";
    internal const string Query = "?mode=apply";
    internal const string PathBase = "/base";
    internal const string RawPath = "/internal/%73ilo";
    internal const string ContentType = "application/json";
    internal const string Chunked = "chunked";
    internal const int TimeMarginMilliseconds = 60000;
    internal const int SingleCapacity = 1;
    private const byte BodyByte = 1;
    private readonly List<Stream> bodies = [];

    internal PeerDiscoveryFixture(int capacity = SingleCapacity)
    {
        Secret = RandomNumberGenerator.GetBytes(PeerDiscoveryProtocol.SecretBytes);
        Sender = new(Secret, TimeProvider.System);
        Receiver = new(Secret, TimeProvider.System, replayCapacity: capacity);
    }

    internal byte[] Secret { get; }
    internal PeerSecurity Sender { get; }
    internal PeerSecurity Receiver { get; }
    internal static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    internal HttpRequestMessage Sign()
    {
        var message = new HttpRequestMessage(HttpMethod.Get, Origin + ReplicaProtocol.DiscoveryPath);
        Sender.Sign(message);
        return message;
    }

    internal static HttpRequest Incoming(HttpRequestMessage message)
    {
        var request = new DefaultHttpContext().Request;
        var uri = message.RequestUri!;
        request.Method = message.Method.Method;
        request.Scheme = uri.Scheme;
        request.Host = new(uri.Authority);
        request.Path = uri.AbsolutePath;
        request.HttpContext.Features.Get<IHttpRequestFeature>()!.RawTarget = uri.PathAndQuery;
        request.Body = Stream.Null;
        foreach (var header in message.Headers)
        { request.Headers[header.Key] = new StringValues(header.Value.ToArray()); }
        return request;
    }

    internal void Mutate(HttpRequest request, PeerDiscoveryMutation mutation)
    {
        switch (mutation)
        {
            case PeerDiscoveryMutation.Time:
                request.Headers[PeerDiscoveryProtocol.TimeHeader] = long.MinValue.ToString(CultureInfo.InvariantCulture);
                break;
            case PeerDiscoveryMutation.Nonce:
                request.Headers[PeerDiscoveryProtocol.NonceHeader] = Guid.NewGuid().ToString(PeerDiscoveryProtocol.NonceFormat);
                break;
            case PeerDiscoveryMutation.Signature:
                request.Headers[PeerDiscoveryProtocol.SignatureHeader] = new string('0', PeerDiscoveryProtocol.HashCharacters);
                break;
            case PeerDiscoveryMutation.Method:
                request.Method = HttpMethods.Post;
                break;
            case PeerDiscoveryMutation.Path:
                request.Path = OtherPath;
                break;
            case PeerDiscoveryMutation.PathBase:
                request.PathBase = PathBase;
                break;
            case PeerDiscoveryMutation.RawPath:
                request.HttpContext.Features.Get<IHttpRequestFeature>()!.RawTarget = RawPath;
                break;
            case PeerDiscoveryMutation.Query:
                request.QueryString = new(Query);
                break;
            case PeerDiscoveryMutation.Recipient:
                request.Host = new(OtherHost);
                break;
            case PeerDiscoveryMutation.ContentType:
                request.ContentType = ContentType;
                break;
            case PeerDiscoveryMutation.TransferEncoding:
                request.Headers[HeaderNames.TransferEncoding] = Chunked;
                break;
            default:
                var body = new MemoryStream([BodyByte]);
                bodies.Add(body);
                request.Body = body;
                request.ContentLength = mutation == PeerDiscoveryMutation.FramedBody ? 1 : null;
                break;
        }
    }

    internal void Resign(HttpRequestMessage message, long time)
    {
        var timestamp = time.ToString(CultureInfo.InvariantCulture);
        var nonce = message.Headers.GetValues(PeerDiscoveryProtocol.NonceHeader).Single();
        var uri = message.RequestUri!;
        var signature = Convert.ToHexStringLower(PeerDiscoverySignature.Compute(Secret, message.Method.Method,
            uri.Authority, uri.AbsolutePath, timestamp, nonce));
        message.Headers.Remove(PeerDiscoveryProtocol.TimeHeader);
        message.Headers.Add(PeerDiscoveryProtocol.TimeHeader, timestamp);
        message.Headers.Remove(PeerDiscoveryProtocol.SignatureHeader);
        message.Headers.Add(PeerDiscoveryProtocol.SignatureHeader, signature);
    }

    public void Dispose()
    {
        foreach (var body in bodies)
        { body.Dispose(); }
        CryptographicOperations.ZeroMemory(Secret);
    }
}
