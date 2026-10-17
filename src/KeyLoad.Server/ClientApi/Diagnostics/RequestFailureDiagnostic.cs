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

/// <summary>Closed request failure dispatch phases suitable for internal diagnostics.</summary>
internal enum RequestFailurePhase
{
    /// <summary>No dispatch marker was set before the failure.</summary>
    None,
    /// <summary>The request failed while dispatching the credential.</summary>
    CredentialDispatch,
    /// <summary>The request failed while dispatching a read.</summary>
    ReadDispatch,
    /// <summary>The request failed while dispatching a command.</summary>
    CommandDispatch
}

/// <summary>Closed exception categories suitable for internal diagnostics.</summary>
internal enum RequestFailureCategory
{
    /// <summary>A KeyLoad domain exception reached the generic failure boundary.</summary>
    KeyLoad,
    /// <summary>A JSON exception reached the generic failure boundary.</summary>
    Json,
    /// <summary>An ASP.NET bad-request exception reached the generic failure boundary.</summary>
    BadHttpRequest,
    /// <summary>An operation cancellation was not caused by caller cancellation.</summary>
    OperationCanceled,
    /// <summary>An argument exception reached the generic failure boundary.</summary>
    Argument,
    /// <summary>An invalid-operation exception reached the generic failure boundary.</summary>
    InvalidOperation,
    /// <summary>An I/O exception reached the generic failure boundary.</summary>
    Io,
    /// <summary>A native Orleans runtime exception reached the generic failure boundary.</summary>
    Orleans,
    /// <summary>A timeout exception reached the generic failure boundary.</summary>
    Timeout,
    /// <summary>An exception outside the known bounded categories reached the boundary.</summary>
    Other
}

/// <summary>Owns the stable event ID and strongly typed, bounded failure log template.</summary>
internal static class RequestFailureDiagnosticLog
{
    private const string FailureLog = "Database request failed at {FailurePhase} with {FailureCategory} for operation {OperationId} at {FailureUtc}.";
    private const int FailureEventId = 1001;

    private static readonly Action<ILogger, RequestFailurePhase, RequestFailureCategory, Guid?, string, Exception?> FailureDelegate =
        LoggerMessage.Define<RequestFailurePhase, RequestFailureCategory, Guid?, string>(
            LogLevel.Error, new EventId(FailureEventId), FailureLog);

    internal static void Failure(ILogger logger, RequestFailurePhase phase, RequestFailureCategory category,
        Guid? operationId, string failureUtc)
        => FailureDelegate(logger, phase, category, operationId, failureUtc, null);
}
