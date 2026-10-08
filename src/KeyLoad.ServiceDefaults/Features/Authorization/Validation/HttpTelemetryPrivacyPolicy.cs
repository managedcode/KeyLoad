using System.Diagnostics;

namespace KeyLoad.ServiceDefaults.Features.Authorization.Contracts;

internal static class HttpTelemetryPrivacyPolicy
{
    internal const string ServerSource = "Microsoft.AspNetCore";
    internal const string ClientSource = "System.Net.Http";
    internal const string ClientOperation = "System.Net.Http.HttpRequestOut";
    internal const string ServerMeter = "Microsoft.AspNetCore.Hosting";
    internal const string ClientMeter = "System.Net.Http";
    internal const string ServerDisplay = "HTTP server request";
    internal const string ClientDisplay = "HTTP client request";
    internal const string MethodTag = "http.request.method";
    internal const string StatusTag = "http.response.status_code";
    internal const string ProtocolTag = "network.protocol.version";
    internal const string MethodGet = "GET";
    internal const string MethodPost = "POST";
    internal const string MethodPut = "PUT";
    internal const string MethodPatch = "PATCH";
    internal const string MethodDelete = "DELETE";
    internal const string MethodHead = "HEAD";
    internal const string MethodOptions = "OPTIONS";
    internal const string MethodConnect = "CONNECT";
    internal const string MethodTrace = "TRACE";
    internal const string Protocol10 = "1.0";
    internal const string Protocol11 = "1.1";
    internal const string Protocol2 = "2";
    internal const string Protocol3 = "3";
    internal const string OtherMethod = "OTHER";
    internal const string PrivacyMeter = "KeyLoad.HttpTelemetryPrivacy";
    internal const string SuppressedCounter = "keyload.http.telemetry.suppressed_spans";
    internal const string ReasonTag = "reason";
    internal const string UnsafeStateReason = "unsafe_state";
    internal const string ExcessStateReason = "state_limit";
    internal const string RuntimeBody = "Runtime event";
    internal const string KeyLoadCategory = "KeyLoad";
    internal const string AspNetCategory = "Microsoft.AspNetCore";
    internal const string OrleansCategory = "Orleans";
    internal const string RuntimeCategory = "Runtime";
    internal const string KeyLoadPrefix = "KeyLoad.";
    internal const string AspNetPrefix = "Microsoft.AspNetCore.";
    internal const string OrleansPrefix = "Orleans.";
    internal const int MinimumStatus = 100;
    internal const int MaximumStatus = 599;
    internal const long CounterIncrement = 1;
    private const int MaximumMethodCharacters = 7;
    private const int MaximumProtocolCharacters = 3;
    private const int MaximumNativeLinks = 1;
    private const int InitialNativeLinkCount = 0;
    internal static string? LinkSuppressionReason(Activity activity)
    {
        var count = InitialNativeLinkCount;
        foreach (var link in activity.Links)
        {
            if (activity.Source.Name != ClientSource || activity.OperationName != ClientOperation || activity.Kind != ActivityKind.Client
                || link.Context.TraceId == default || link.Context.SpanId == default || link.Context.IsRemote
                || link.Context.TraceState is not null || link.Context.TraceFlags is not (ActivityTraceFlags.None or ActivityTraceFlags.Recorded)
                || (link.Tags?.Any() ?? false))
            { return UnsafeStateReason; }
            if (++count > MaximumNativeLinks)
            { return ExcessStateReason; }
        }
        return null;
    }
    internal static bool IsHttp(string source) => source is ServerSource or ClientSource;
    internal static string NormalizeMethod(object? value) => value is string { Length: <= MaximumMethodCharacters } text ? text switch
    {
        MethodGet => MethodGet,
        MethodPost => MethodPost,
        MethodPut => MethodPut,
        MethodPatch => MethodPatch,
        MethodDelete => MethodDelete,
        MethodHead => MethodHead,
        MethodOptions => MethodOptions,
        MethodConnect => MethodConnect,
        MethodTrace => MethodTrace,
        _ => OtherMethod
    } : OtherMethod;
    internal static string? NormalizeProtocol(object? value) => value is string { Length: <= MaximumProtocolCharacters } text ? text switch
    {
        Protocol10 => Protocol10,
        Protocol11 => Protocol11,
        Protocol2 => Protocol2,
        Protocol3 => Protocol3,
        _ => null
    } : null;
    internal static string Category(string? category)
    {
        if (category?.StartsWith(KeyLoadPrefix, StringComparison.Ordinal) == true)
        { return KeyLoadCategory; }
        if (category?.StartsWith(AspNetPrefix, StringComparison.Ordinal) == true)
        { return AspNetCategory; }
        if (category?.StartsWith(OrleansPrefix, StringComparison.Ordinal) == true)
        { return OrleansCategory; }
        return RuntimeCategory;
    }
}
