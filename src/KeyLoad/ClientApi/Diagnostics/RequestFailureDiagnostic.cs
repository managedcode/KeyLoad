using System.Globalization;

namespace KeyLoad.Server;

/// <summary>Stores and emits bounded dispatch evidence only when a request fails.</summary>
internal static class RequestFailureDiagnostic
{
    private const string FailureUtcFormat = "O";
    private static readonly object /* KLD0041: Use a private reference-identity token as the native HttpContext.Items key so unrelated middleware cannot collide with the failure phase. */ PhaseItemKey = (new() /* KLD0041: Use a private reference-identity token as the native HttpContext.Items key so unrelated middleware cannot collide with the failure phase. */);
    private static readonly object /* KLD0041: Box this closed credential dispatch phase once at type initialization and reuse it in HttpContext.Items instead of boxing on every request. */ CredentialDispatchValue = (RequestFailurePhase.CredentialDispatch /* KLD0042: Box this closed credential dispatch phase once at type initialization and reuse it in HttpContext.Items instead of boxing on every request. */);
    private static readonly object /* KLD0041: Box this closed read dispatch phase once at type initialization and reuse it in HttpContext.Items instead of boxing on every request. */ ReadDispatchValue = (RequestFailurePhase.ReadDispatch /* KLD0042: Box this closed read dispatch phase once at type initialization and reuse it in HttpContext.Items instead of boxing on every request. */);
    private static readonly object /* KLD0041: Box this closed command dispatch phase once at type initialization and reuse it in HttpContext.Items instead of boxing on every request. */ CommandDispatchValue = (RequestFailurePhase.CommandDispatch /* KLD0042: Box this closed command dispatch phase once at type initialization and reuse it in HttpContext.Items instead of boxing on every request. */);

    /// <summary>Marks the request-local credential dispatch boundary using a preboxed closed phase.</summary>
    /// <param name="context">The current public HTTP request.</param>
    internal static void MarkCredentialDispatch(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        ((context.Items /* KLD0041: ASP.NET exposes the request-local failure-phase storage as IDictionary<object, object?>; only this owner's private key is accessed here. */)[(PhaseItemKey /* KLD0041: Address only this diagnostic owner's private reference-identity key in ASP.NET HttpContext.Items. */)] /* KLD0041: Write the already boxed closed dispatch phase to the ASP.NET request-local diagnostic slot. */) = (CredentialDispatchValue /* KLD0041: Reuse the preboxed CredentialDispatch phase in the ASP.NET request-local slot without allocating another enum box. */);
    }

    /// <summary>Marks the operation dispatch boundary using a preboxed closed phase.</summary>
    /// <param name="context">The current public HTTP request.</param>
    /// <param name="isCommand">Whether the operation mutates database state.</param>
    internal static void MarkOperationDispatch(HttpContext context, bool isCommand)
    {
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        ((context.Items /* KLD0041: ASP.NET exposes the request-local failure-phase storage as IDictionary<object, object?>; only this owner's private key is accessed here. */)[(PhaseItemKey /* KLD0041: Address only this diagnostic owner's private reference-identity key in ASP.NET HttpContext.Items. */)] /* KLD0041: Write the already boxed closed dispatch phase to the ASP.NET request-local diagnostic slot. */) = isCommand ? (CommandDispatchValue /* KLD0041: Reuse the preboxed CommandDispatch phase in the ASP.NET request-local slot without allocating another enum box. */) : (ReadDispatchValue /* KLD0041: Reuse the preboxed ReadDispatch phase in the ASP.NET request-local slot without allocating another enum box. */);
    }

    /// <summary>Logs closed failure evidence without attaching an exception or arbitrary caller data.</summary>
    /// <param name="logger">The middleware logger.</param>
    /// <param name="context">The failed public HTTP request.</param>
    /// <param name="error">The caught error, used only for closed type classification.</param>
    /// <param name="clock">The middleware owner's clock for the failure timestamp.</param>
    internal static void LogFailure(ILogger logger, HttpContext context, Exception error, TimeProvider clock)
    {
        var phase = ((context.Items /* KLD0041: ASP.NET exposes the request-local failure-phase storage as IDictionary<object, object?>; only this owner's private key is accessed here. */).TryGetValue((PhaseItemKey /* KLD0041: Address only this diagnostic owner's private reference-identity key in ASP.NET HttpContext.Items. */), out var value) /* KLD0041: Read the diagnostic owner's native HttpContext.Items slot, then validate its closed phase enum before logging. */)
            && (value is RequestFailurePhase knownPhase /* KLD0042: Recover only the closed RequestFailurePhase enum from the request-local Items slot and reject unknown values before logging. */) && Enum.IsDefined(knownPhase)
            ? knownPhase
            : RequestFailurePhase.None;
        RequestFailureDiagnosticLog.Failure(logger, phase, Category(error), OperationResponseHeaders.RequestId(context),
            clock.GetUtcNow().ToString(FailureUtcFormat, CultureInfo.InvariantCulture));
    }

    private static RequestFailureCategory Category(Exception error) => error switch
    {
        KeyLoadException => RequestFailureCategory.KeyLoad,
        System.Text.Json.JsonException => RequestFailureCategory.Json,
        BadHttpRequestException => RequestFailureCategory.BadHttpRequest,
        OperationCanceledException => RequestFailureCategory.OperationCanceled,
        ArgumentException => RequestFailureCategory.Argument,
        InvalidOperationException => RequestFailureCategory.InvalidOperation,
        IOException => RequestFailureCategory.Io,
        global::Orleans.Runtime.OrleansException => RequestFailureCategory.Orleans,
        TimeoutException => RequestFailureCategory.Timeout,
        _ => RequestFailureCategory.Other
    };
}
