namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentEndpointGuardHandler(Uri endpoint, HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var target = request.RequestUri;
        if (target is null || target.Scheme != endpoint.Scheme || target.Port != endpoint.Port ||
            !string.Equals(target.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException(KurrentConstants.EndpointRedirect);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
