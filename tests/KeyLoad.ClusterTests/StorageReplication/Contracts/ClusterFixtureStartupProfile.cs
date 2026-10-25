namespace KeyLoad.ClusterTests.StorageReplication;

internal static class ClusterFixtureStartupProfile
{
    internal const string EnabledSetting = "ASPIRE_PROFILING_ENABLED";
    internal const string SessionSetting = "ASPIRE_PROFILING_SESSION_ID";
    internal const string SessionTag = "aspire.profiling.session_id";
    internal const string ActivitySource = "Aspire.Hosting.Profiling";
    internal const string EnsureOperation = "aspire.hosting.dcp.ensure_kubernetes_client";
    internal const string ApiOperation = "aspire.hosting.dcp.kubernetes_api";
    internal const string ConfigExistsTag = "aspire.dcp.kubeconfig.exists";
    internal const string LockWaitTag = "aspire.dcp.kubeconfig.lock_wait_ms";
    internal const string FileWaitTag = "aspire.dcp.kubeconfig.file_wait_ms";
    internal const string BuildDurationTag = "aspire.dcp.kubeconfig.build_duration_ms";
    internal const string ReadDurationTag = "aspire.dcp.kubeconfig.read_duration_ms";
    internal const string ApiRetryTag = "aspire.dcp.api.retry_count";
    internal const string LockEvent = "aspire.hosting.dcp.kubeconfig_lock_acquired";
    internal const string FileEvent = "aspire.hosting.dcp.kubeconfig_file_detected";
    internal const string ReadEvent = "aspire.hosting.dcp.kubeconfig_read_complete";
    internal const string ClientEvent = "aspire.hosting.dcp.kubernetes_client_created";
    internal const string ReadyEvent = "aspire.hosting.dcp.kubernetes_client_ready";
    internal const string TimeoutEvent = "aspire.hosting.dcp.kubernetes_api.timeout";
    internal const string EvidenceFile = "aspire-startup-profile.log";
}
