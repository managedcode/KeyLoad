using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace KeyLoad.Replication;

internal static class PeerDiscoveryRequest
{
    private const string EmptyContentLength = "0";
    private static readonly object RejectedBody = new();

    internal static bool Valid(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        return request.Method.Method == HttpMethods.Get && request.Content is null && uri is { IsAbsoluteUri: true }
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.AbsolutePath == ReplicaProtocol.DiscoveryPath && uri.Query.Length == 0 && uri.Fragment.Length == 0
            && uri.UserInfo.Length == 0 && ValidAuthority(uri.Authority)
            && (request.Headers.Host is null || request.Headers.Host == uri.Authority)
            && !request.Headers.Contains(HeaderNames.TransferEncoding);
    }

    internal static bool Valid(HttpRequest request)
    {
        var rawTarget = request.HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget;
        return request.Method == HttpMethods.Get && request.Path.Value == ReplicaProtocol.DiscoveryPath && !request.PathBase.HasValue
            && !request.QueryString.HasValue && (string.IsNullOrEmpty(rawTarget) || rawTarget == ReplicaProtocol.DiscoveryPath)
            && (request.Scheme == Uri.UriSchemeHttp || request.Scheme == Uri.UriSchemeHttps)
            && ValidAuthority(request.Host.Value) && BodylessHeaders(request)
            && !request.HttpContext.Items.ContainsKey(RejectedBody);
    }

    private static bool BodylessHeaders(HttpRequest request)
    {
        var length = request.Headers[HeaderNames.ContentLength];
        return (length.Count == 0 || length.Count == 1 && length[0] == EmptyContentLength)
            && !request.Headers.ContainsKey(HeaderNames.ContentType) && !request.Headers.ContainsKey(HeaderNames.TransferEncoding);
    }

    internal static async Task<bool> EmptyBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var probe = new byte[PeerDiscoveryProtocol.BodyProbeBytes];
        try
        {
            if (await request.Body.ReadAsync(probe, cancellationToken) == 0)
            { return true; }
        }
        catch (Exception error) when (error is IOException or BadHttpRequestException) { }
        request.HttpContext.Items[RejectedBody] = true;
        return false;
    }

    private static bool ValidAuthority(string? authority)
    {
        return !string.IsNullOrEmpty(authority) && authority.Length <= PeerDiscoveryProtocol.MaximumAuthorityCharacters
            && !authority.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '/' or '\\' or '@' or '?' or '#')
            && Uri.TryCreate(Uri.UriSchemeHttp + Uri.SchemeDelimiter + authority, UriKind.Absolute, out var uri)
            && uri.Host.Length > 0 && uri.UserInfo.Length == 0;
    }
}
