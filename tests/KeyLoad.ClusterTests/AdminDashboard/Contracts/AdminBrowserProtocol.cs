namespace KeyLoad.ClusterTests.AdminDashboard;

internal static class AdminBrowserProtocol
{
    internal const string BrowserEnvironment = "KEYLOAD_ADMIN_CHROME_PATH";
    internal const string EvidenceDirectory = "artifacts/qualification/admin-dashboard";
    internal const int MobileWidth = 390;
    internal const int DesktopWidth = 1_920;
    internal const int Height = 1_080;
    internal const float ActionTimeout = 30_000;
    internal static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);
}
