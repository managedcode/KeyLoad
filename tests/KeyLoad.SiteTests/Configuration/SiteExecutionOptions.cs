namespace KeyLoad.SiteTests.BenchmarkComparisons;

[KeyLoad.ConfigurationOptions]
internal sealed class SiteExecutionOptions
{
    public TimeSpan BrowserCommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan BrowserRetry { get; set; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan BrowserStartupTimeout { get; set; } = TimeSpan.FromSeconds(20);
    public TimeSpan NavigationPollDelay { get; set; } = TimeSpan.FromMilliseconds(20);
    public TimeSpan NoScriptPoll { get; set; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan SceneIdleWait { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan SceneSettleWait { get; set; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan ChildDeadline { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan ChildPoll { get; set; } = TimeSpan.FromMilliseconds(10);
    public TimeSpan ChildQueueDeadline { get; set; } = TimeSpan.FromSeconds(1);
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(3);
    public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan StaticHostTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan AdmissionDeadline { get; set; } = TimeSpan.FromMinutes(20);
    public TimeSpan GitHubDeadline { get; set; } = TimeSpan.FromMinutes(120);
    public TimeSpan IsolatedProcessDeadline { get; set; } = TimeSpan.FromSeconds(300);
    public int BrowserSessionCapacity { get; set; } = 1;
    public int MaximumQueued { get; set; } = 64;
    public int HeavyChildCapacity { get; set; } = 2;
    public int ProcessOutputBufferCharacters { get; set; } = 4096;
}
