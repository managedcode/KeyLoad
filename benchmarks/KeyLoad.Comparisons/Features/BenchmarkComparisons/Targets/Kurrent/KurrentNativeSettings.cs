using KurrentDB.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentNativeSettings
{
    public static KurrentDBClientSettings CreateWriter(string connectionString)
    {
        var settings = KurrentDBClientSettings.Create(connectionString);
        ValidateTls(settings);
        settings.ConnectivitySettings.NodePreference = NodePreference.Leader;
        return settings;
    }

    public static KurrentDBClientSettings CreateDirectNode(string connectionString, Uri endpoint)
    {
        var settings = KurrentDBClientSettings.Create(connectionString);
        ValidateTls(settings);
        var connectivity = settings.ConnectivitySettings;
        if (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ComparisonFailureException(KurrentConstants.EndpointSecurityMismatch);
        }

        var expectedInsecure = endpoint.Scheme == Uri.UriSchemeHttp;
        if (connectivity.Insecure != expectedInsecure)
        {
            throw new ComparisonFailureException(KurrentConstants.EndpointSecurityMismatch);
        }

        connectivity.DnsGossipSeeds = [];
        connectivity.IpGossipSeeds = [];
        connectivity.Address = endpoint;
        connectivity.NodePreference = NodePreference.Random;
        BindHandler(settings, endpoint);
        return settings;
    }

    private static void ValidateTls(KurrentDBClientSettings settings)
    {
        if (!settings.ConnectivitySettings.TlsVerifyCert)
        {
            throw new ComparisonFailureException(KurrentConstants.TlsVerificationDisabled);
        }
    }

    private static void BindHandler(KurrentDBClientSettings settings, Uri endpoint)
    {
        var factory = settings.CreateHttpMessageHandler ?? throw new ComparisonFailureException(KurrentConstants.EndpointHandlerUnavailable);
        settings.CreateHttpMessageHandler = () => WrapHandler(factory(), endpoint);
    }

    private static KurrentEndpointGuardHandler WrapHandler(HttpMessageHandler handler, Uri endpoint)
    {
        if (handler is not SocketsHttpHandler sockets)
        {
            handler.Dispose();
            throw new ComparisonFailureException(KurrentConstants.EndpointHandlerUnavailable);
        }
        sockets.AllowAutoRedirect = false;
        return new KurrentEndpointGuardHandler(endpoint, sockets);
    }
}
