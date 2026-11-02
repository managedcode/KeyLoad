namespace KeyLoad.Server;

/// <summary>Publishes one operation identity safely across the execution response-header start boundary.</summary>
internal static class OperationResponseHeaders
{
    private const string InvalidPublication = "The operation response identity cannot be published in this request state.";

    /// <summary>Captures one request-local identity holder before execution handlers can start the response.</summary>
    /// <param name="context">The new public HTTP request.</param>
    internal static void Register(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        if (((context.Items /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */).ContainsKey(ServerProtocol.ExecutionRequestItem) /* KLD0041: Reject duplicate header publication registration using the owned key in ASP.NET's native request-local Items dictionary. */))
        {
            throw new InvalidOperationException(InvalidPublication);
        }
        var state = new HeaderState(context.Response);
        ((context.Items /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */)[ServerProtocol.ExecutionRequestItem] /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */) = state;
        context.Response.OnStarting(static value => (((HeaderState)value /* KLD0042: Recover the exact HeaderState reference passed to ASP.NET OnStarting so response headers publish at the framework-owned start boundary. */)).StartAsync(), state) /* KLD0041: ASP.NET OnStarting accepts object state; pass the existing HeaderState to a static callback without a closure allocation. */;
    }

    /// <summary>Atomically publishes the actual issued operation GUID immediately before actor dispatch.</summary>
    /// <param name="context">The registered public HTTP request.</param>
    /// <param name="requestId">The fresh server-generated operation identity.</param>
    internal static void Publish(HttpContext context, Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        if (requestId == Guid.Empty || (((context.Items /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */)[ServerProtocol.ExecutionRequestItem] /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */) is not HeaderState state /* KLD0042: Require this request's concrete HeaderState from the native Items slot before publishing or observing the actual operation GUID. */))
        {
            throw new InvalidOperationException(InvalidPublication);
        }
        state.Publish(requestId);
    }

    /// <summary>Reads the same identity used by safe errors, structured results and the optional HTTP header.</summary>
    /// <param name="context">The request whose identity is required.</param>
    /// <returns>The actual published GUID, or null before dispatch.</returns>
    internal static Guid? RequestId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        return ((((context.Items /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */)[ServerProtocol.ExecutionRequestItem] /* KLD0041: Store or retrieve the existing HeaderState reference under this owner's key in ASP.NET HttpContext.Items; no operation GUID is boxed. */) as HeaderState /* KLD0042: Require this request's concrete HeaderState from the native Items slot before publishing or observing the actual operation GUID. */))?.RequestId;
    }

    private sealed class HeaderState(HttpResponse response)
    {
        private Identity? identity;

        internal Guid? RequestId => Volatile.Read(ref identity)?.Value;

        internal void Publish(Guid requestId)
        {
            if (Interlocked.CompareExchange(ref identity, new Identity(requestId), null) is not null)
            {
                throw new InvalidOperationException(InvalidPublication);
            }
        }

        internal Task StartAsync()
        {
            var published = Volatile.Read(ref identity);
            if (published is not null)
            {
                response.Headers[ServerProtocol.RequestHeader] = published.Value.ToString(OrleansNodeProtocol.GuidFormat);
            }
            return Task.CompletedTask;
        }
    }

    private sealed record Identity(Guid Value);
}
