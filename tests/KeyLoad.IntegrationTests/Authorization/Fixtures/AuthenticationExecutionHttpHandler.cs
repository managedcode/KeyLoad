namespace KeyLoad.IntegrationTests.Authorization;

internal sealed class AuthenticationExecutionHttpHandler : DelegatingHandler
{
    private int requests;
    internal AuthenticationExecutionHttpHandler(HttpMessageHandler inner) : base(inner) { }
    internal int Requests => Volatile.Read(ref requests);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref requests);
        return base.SendAsync(request, cancellationToken);
    }
}
