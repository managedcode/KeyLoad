namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class OperationalPolicyMetadataNames
{
    internal const string Channel = "System.Threading.Channels.Channel";
    internal const string BoundedChannel = "System.Threading.Channels.BoundedChannelOptions";
    internal const string CreateBounded = "CreateBounded";
    internal const string Capacity = "Capacity";
    internal const string HttpClient = "System.Net.Http.HttpClient";
    internal const string HttpClientHandler = "System.Net.Http.HttpClientHandler";
    internal const string SocketsHttpHandler = "System.Net.Http.SocketsHttpHandler";
    internal const string Socket = "System.Net.Sockets.Socket";
    internal const string RetryOptions = "Polly.Retry.RetryStrategyOptions";
    internal const string GenericRetryOptions = "Polly.Retry.RetryStrategyOptions`1";
    internal const string Timeout = "Timeout";
    internal const string ConnectTimeout = "ConnectTimeout";
    internal const string PooledConnectionIdleTimeout = "PooledConnectionIdleTimeout";
    internal const string PooledConnectionLifetime = "PooledConnectionLifetime";
    internal const string MaxConnectionsPerServer = "MaxConnectionsPerServer";
    internal const string MaxResponseHeadersLength = "MaxResponseHeadersLength";
    internal const string ResponseDrainTimeout = "ResponseDrainTimeout";
    internal const string MaxResponseDrainSize = "MaxResponseDrainSize";
    internal const string Expect100ContinueTimeout = "Expect100ContinueTimeout";
    internal const string ReceiveTimeout = "ReceiveTimeout";
    internal const string SendTimeout = "SendTimeout";
    internal const string ReceiveBufferSize = "ReceiveBufferSize";
    internal const string SendBufferSize = "SendBufferSize";
    internal const string MaxRetryAttempts = "MaxRetryAttempts";
    internal const string Delay = "Delay";
    internal const int MutexPermits = 1;
}
