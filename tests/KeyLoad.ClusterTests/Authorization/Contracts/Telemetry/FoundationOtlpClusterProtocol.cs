namespace KeyLoad.ClusterTests.Authorization;

internal static class FoundationOtlpClusterProtocol
{
    internal static readonly string[] Nodes = ["node1", "node2", "node3"];
    internal const string BrowserTokenQuery = "t";
    internal const string BrowserTokenProperty = "token";
    internal const string ApiKeyProperty = "apiKey";
    internal const string ResourceQuery = "resource";
    internal const string TraceQuery = "traceId";
    internal const string LimitQuery = "limit";
    internal const string TotalCountProperty = "totalCount";
    internal const string ReturnedCountProperty = "returnedCount";
    internal const string DataProperty = "data";
    internal const string TraceIdProperty = "traceId";
    internal const string SeverityNumberProperty = "severityNumber";
    internal const string BodyProperty = "body";
    internal const string StringValueProperty = "stringValue";
    internal const string KindProperty = "kind";
    internal const string NameProperty = "name";
    internal const string ResourceLogsProperty = "resourceLogs";
    internal const string ResourceSpansProperty = "resourceSpans";
    internal const string ResourceProperty = "resource";
    internal const string AttributesProperty = "attributes";
    internal const string KeyProperty = "key";
    internal const string ValueProperty = "value";
    internal const string ServiceNameAttribute = "service.name";
    internal const string ServiceInstanceAttribute = "service.instance.id";
    internal const string ScopeLogsProperty = "scopeLogs";
    internal const string ScopeSpansProperty = "scopeSpans";
    internal const string LogRecordsProperty = "logRecords";
    internal const string SpansProperty = "spans";
    internal const int WarningSeverity = 13;
    internal const int ServerSpanKind = 2;
    internal const string ParentOperation = "Foundation telemetry caller";
    internal const string Failure = "The original Orleans telemetry receiver contract failed.";
    internal const string Header = "X-KeyLoad-Private";
    internal const string HeaderCanary = "cluster-execution-private-header";
    internal const string ApiKeyHeader = "X-API-Key";
    internal const string ExchangePath = "/api/telemetry/validateToken";
    internal const string SpanPath = "/api/telemetry/spans";
    internal const string LogPath = "/api/telemetry/logs";
}
