namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryTestProtocol
{
    internal const string CallerParent = "HTTP caller";
    internal const string Bearer = "Bearer";
    internal const string PrivateHeader = "X-KeyLoad-Private";
    internal const string HeaderCanary = "header-canary";
    internal const int RequestCount = 2;
    internal const int HealthyCount = 1;
    internal const int CatalogCount = 1;
    internal const int IndexCount = 0;
    internal const long SchemaVersion = 1;
    internal const string Route = "/telemetry/catalog/{subject}";
    internal const string Target = "/telemetry/catalog/path-canary?token=query-canary";
    internal const string Administrator = "root";
    internal const string AdministratorKey = "root.unit-test-credential-32-characters";
    internal const string Member = "telemetry-member";
    internal const string MemberKey = "telemetry-member.credential-canary-32-characters";
    internal const string Resource = "telemetry-catalog";
    internal const string BodyCanary = "body-canary";
    internal const string ExceptionCanary = "exception-canary";
    internal const string TraceStateCanary = "private=tracestate-canary";
    internal const string ScopeCanary = "scope-canary";
    internal const string TagCanary = "tag-canary";
    internal const string BaggageCanary = "baggage-canary";
    internal const string FailureTemplate = "Execution catalog denied {Payload}";
    internal const string HealthyTemplate = "Execution catalog returned {Payload}";
    internal const string LoggerCategory = "KeyLoad.Authorization.TelemetryOperations";
    internal const string CanaryTag = "tenant";
    internal const string BaggageKey = "private";
    internal const string HttpServerSource = "Microsoft.AspNetCore";
    internal const string HttpClientSource = "System.Net.Http";
    internal const string ServerDuration = "http.server.request.duration";
    internal const string ClientDuration = "http.client.request.duration";
    internal const string ScopeDenial = "Cluster administration is required.";
    internal const int DeniedEventId = 601;
    internal const int HealthyEventId = 602;
    internal const string FlushFailure = "Execution telemetry exporters did not flush before the bounded deadline.";
    internal const string CaptureFailure = "Execution telemetry capture exceeded its validated capacity.";
    internal static readonly string[] Canaries = ["path-canary", "query-canary", MemberKey,
        AdministratorKey, BodyCanary, HeaderCanary, ExceptionCanary, TraceStateCanary, ScopeCanary, TagCanary, BaggageCanary];
}
