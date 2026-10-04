namespace KeyLoad.Server;

/// <summary>Publishes one operation identity safely across the native response-header start boundary.</summary>
internal static class OperationResponseHeaders
{
    private const string InvalidPublication = "The operation response identity cannot be published in this request state.";

    /// <summary>Captures one request-local identity holder before native handlers can start the response.</summary>
    /// <param name="context">The new public HTTP request.</param>
    internal static void Register(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Items.ContainsKey(ServerProtocol.ExecutionRequestItem))
        { throw new InvalidOperationException(InvalidPublication); }
        var state = new HeaderState(context.Response);
        context.Items[ServerProtocol.ExecutionRequestItem] = state;
        context.Response.OnStarting(static value => ((HeaderState)value).StartAsync(), state);
    }

    /// <summary>Atomically publishes the actual signed operation GUID immediately before actor dispatch.</summary>
    /// <param name="context">The registered public HTTP request.</param>
    /// <param name="requestId">The fresh server-generated operation identity.</param>
    internal static void Publish(HttpContext context, Guid requestId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (requestId == Guid.Empty || context.Items[ServerProtocol.ExecutionRequestItem] is not HeaderState state)
        { throw new InvalidOperationException(InvalidPublication); }
        state.Publish(requestId);
    }

    /// <summary>Reads the same identity used by safe errors, structured results and the optional HTTP header.</summary>
    /// <param name="context">The request whose identity is required.</param>
    /// <returns>The actual published GUID, or null before dispatch.</returns>
    internal static Guid? RequestId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return (context.Items[ServerProtocol.ExecutionRequestItem] as HeaderState)?.RequestId;
    }

    private sealed class HeaderState(HttpResponse response)
    {
        private Identity? identity;

        internal Guid? RequestId => Volatile.Read(ref identity)?.Value;

        internal void Publish(Guid requestId)
        {
            if (Interlocked.CompareExchange(ref identity, new Identity(requestId), null) is not null)
            { throw new InvalidOperationException(InvalidPublication); }
        }

        internal Task StartAsync()
        {
            var published = Volatile.Read(ref identity);
            if (published is not null)
            { response.Headers[ServerProtocol.RequestHeader] = published.Value.ToString(OrleansNodeProtocol.GuidFormat); }
            return Task.CompletedTask;
        }
    }

    private sealed record Identity(Guid Value);
}
