using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace KeyLoad.Replication;

internal static class PeerDiscoveryRequest
{
    private const int EmptyUriComponentLength = 0;
    private const int NoContentLengthHeader = 0;
    private const int SingleContentLengthHeader = 1;
    private const int FirstHeaderValueIndex = 0;
    private const int EndOfRequestBody = 0;
    private const char ForwardSlash = '/';
    private const char BackSlash = '\\';
    private const char UserInfoSeparator = '@';
    private const char QuerySeparator = '?';
    private const char FragmentSeparator = '#';

    private const string EmptyContentLength = "0";
    private static readonly object RejectedBody = new();

    internal static bool Valid(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        return request.Method.Method == HttpMethods.Get && request.Content is null && uri is { IsAbsoluteUri: true }
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.AbsolutePath == ReplicaProtocol.DiscoveryPath && uri.Query.Length == EmptyUriComponentLength && uri.Fragment.Length == EmptyUriComponentLength
            && uri.UserInfo.Length == EmptyUriComponentLength && ValidAuthority(uri.Authority)
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
        return (length.Count == NoContentLengthHeader || length.Count == SingleContentLengthHeader && length[FirstHeaderValueIndex] == EmptyContentLength)
            && !request.Headers.ContainsKey(HeaderNames.ContentType) && !request.Headers.ContainsKey(HeaderNames.TransferEncoding);
    }

    internal static async Task<bool> EmptyBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var probe = new byte[PeerDiscoveryProtocol.BodyProbeBytes];
        try
        {
            if (await request.Body.ReadAsync(probe, cancellationToken) == EndOfRequestBody)
            { return true; }
        }
        catch (Exception error) when (error is IOException or BadHttpRequestException) { }
        request.HttpContext.Items[RejectedBody] = true;
        return false;
    }

    private static bool ValidAuthority(string? authority)
    {
        return !string.IsNullOrEmpty(authority) && authority.Length <= PeerDiscoveryProtocol.MaximumAuthorityCharacters
            && !authority.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is ForwardSlash or BackSlash or UserInfoSeparator or QuerySeparator or FragmentSeparator)
            && Uri.TryCreate(Uri.UriSchemeHttp + Uri.SchemeDelimiter + authority, UriKind.Absolute, out var uri)
            && uri.Host.Length > EmptyUriComponentLength && uri.UserInfo.Length == EmptyUriComponentLength;
    }
}
