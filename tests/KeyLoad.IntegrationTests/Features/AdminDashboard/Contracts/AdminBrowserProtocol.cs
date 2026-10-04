namespace KeyLoad.IntegrationTests.Features.AdminDashboard;

internal static class AdminBrowserProtocol
{
    internal const string BrowserEnvironment = "KEYLOAD_ADMIN_CHROME_PATH";
    internal const string ProfilePrefix = "keyload-admin-chrome-";
    internal const string EndpointFile = "DevToolsActivePort";
    internal const string TargetsPath = "/json/list";
    internal const string SocketProperty = "webSocketDebuggerUrl";
    internal const string MissingBrowser = "Real Chrome is required for AdminDashboard qualification.";
    internal const string BrowserFailure = "The real administration browser operation failed.";
    internal const string EvidenceDirectory = "artifacts/qualification/admin-dashboard";
    internal const string RuntimeEvaluate = "Runtime.evaluate";
    internal const string PageNavigate = "Page.navigate";
    internal const string SetMetrics = "Emulation.setDeviceMetricsOverride";
    internal const string CaptureScreenshot = "Page.captureScreenshot";
    internal const string IdProperty = "id";
    internal const string ErrorProperty = "error";
    internal const string ResultProperty = "result";
    internal const string ExceptionProperty = "exceptionDetails";
    internal const string ValueProperty = "value";
    internal const string TypeProperty = "type";
    internal const string PageType = "page";
    internal const string GuidFormat = "N";
    internal const string LoopbackPrefix = "http://127.0.0.1:";
    internal const int MaximumReplyBytes = 4_194_304;
    internal const int ReceiveBufferBytes = 16_384;
    internal const int PollMilliseconds = 100;
    internal const int MobileWidth = 390;
    internal const int DesktopWidth = 1_440;
    internal const int Height = 900;
    internal static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);
}
