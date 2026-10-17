namespace KeyLoad.UnitTests.Features.Authorization;

internal static class AuthorizationTelemetryTestProtocol
{
    internal const string CallerParent = "HTTP caller";
    internal const string Bearer = "Bearer";
    internal const string PrivateHeader = "X-KeyLoad-Private";
    internal const string HeaderCanary = "kl015-header-canary";
    internal const int RequestCount = 2;
    internal const int HealthyCount = 1;
    internal const int CatalogCount = 1;
    internal const int IndexCount = 0;
    internal const long SchemaVersion = 1;
    internal const string Route = "/v1/telemetry/catalog/{subject}";
    internal const string Target = "/v1/telemetry/catalog/kl015-path-canary?token=kl015-query-canary";
    internal const string Administrator = "root";
    internal const string AdministratorKey = "root.unit-test-credential-32-characters";
    internal const string Member = "telemetry-member";
    internal const string MemberKey = "telemetry-member.kl015-credential-canary-32-characters";
    internal const string Resource = "telemetry-catalog";
    internal const string BodyCanary = "kl015-body-canary";
    internal const string ExceptionCanary = "kl015-exception-canary";
    internal const string TraceStateCanary = "private=kl015-tracestate-canary";
    internal const string ScopeCanary = "kl015-scope-canary";
    internal const string TagCanary = "kl015-tag-canary";
    internal const string BaggageCanary = "kl015-baggage-canary";
    internal const string FailureTemplate = "Native catalog denied {Payload}";
    internal const string HealthyTemplate = "Native catalog returned {Payload}";
    internal const string LoggerCategory = "KeyLoad.Authorization.TelemetryRegression";
    internal const string CanaryTag = "tenant";
    internal const string BaggageKey = "private";
    internal const string HttpServerSource = "Microsoft.AspNetCore";
    internal const string HttpClientSource = "System.Net.Http";
    internal const string ServerDuration = "http.server.request.duration";
    internal const string ClientDuration = "http.client.request.duration";
    internal const string ScopeDenial = "Cluster administration is required.";
    internal const int DeniedEventId = 601;
    internal const int HealthyEventId = 602;
    internal const string FlushFailure = "Native telemetry exporters did not flush before the bounded deadline.";
    internal const string CaptureFailure = "Native telemetry capture exceeded its validated capacity.";
    internal static readonly string[] Canaries = ["kl015-path-canary", "kl015-query-canary", MemberKey,
        AdministratorKey, BodyCanary, HeaderCanary, ExceptionCanary, TraceStateCanary, ScopeCanary, TagCanary, BaggageCanary];
}
