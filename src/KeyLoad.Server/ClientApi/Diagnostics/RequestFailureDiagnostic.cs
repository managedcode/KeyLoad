using System.Globalization;

namespace KeyLoad.Server;

/// <summary>Stores and emits bounded dispatch evidence only when a request fails.</summary>
internal static class RequestFailureDiagnostic
{
    private const string FailureUtcFormat = "O";
    private static readonly object PhaseItemKey = new();
    private static readonly object CredentialDispatchValue = RequestFailurePhase.CredentialDispatch;
    private static readonly object ReadDispatchValue = RequestFailurePhase.ReadDispatch;
    private static readonly object CommandDispatchValue = RequestFailurePhase.CommandDispatch;

    /// <summary>Marks the request-local credential dispatch boundary using a preboxed closed phase.</summary>
    /// <param name="context">The current public HTTP request.</param>
    internal static void MarkCredentialDispatch(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[PhaseItemKey] = CredentialDispatchValue;
    }

    /// <summary>Marks the operation dispatch boundary using a preboxed closed phase.</summary>
    /// <param name="context">The current public HTTP request.</param>
    /// <param name="isCommand">Whether the operation mutates database state.</param>
    internal static void MarkOperationDispatch(HttpContext context, bool isCommand)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Items[PhaseItemKey] = isCommand ? CommandDispatchValue : ReadDispatchValue;
    }

    /// <summary>Logs closed failure evidence without attaching an exception or arbitrary caller data.</summary>
    /// <param name="logger">The middleware logger.</param>
    /// <param name="context">The failed public HTTP request.</param>
    /// <param name="error">The caught error, used only for closed type classification.</param>
    /// <param name="clock">The middleware owner's clock for the failure timestamp.</param>
    internal static void LogFailure(ILogger logger, HttpContext context, Exception error, TimeProvider clock)
    {
        var phase = context.Items.TryGetValue(PhaseItemKey, out var value)
            && value is RequestFailurePhase knownPhase && Enum.IsDefined(knownPhase)
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
