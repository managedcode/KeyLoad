using Microsoft.Extensions.Options;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

[KeyLoad.ConfigurationBinding]
internal static class SiteExecutionConfiguration
{
    internal static IOptions<SiteExecutionOptions> Options { get; } = Bind();

    private static IOptions<SiteExecutionOptions> Bind()
    {
        var policy = new SiteExecutionOptions();
        TimeSpan[] durations = [policy.BrowserCommandTimeout, policy.BrowserRetry,
            policy.BrowserStartupTimeout, policy.NavigationPollDelay, policy.NoScriptPoll,
            policy.SceneIdleWait, policy.SceneSettleWait, policy.ChildDeadline, policy.ChildPoll,
            policy.ChildQueueDeadline, policy.CleanupTimeout, policy.NodeTimeout,
            policy.StaticHostTimeout, policy.AdmissionDeadline, policy.GitHubDeadline,
            policy.IsolatedProcessDeadline];
        foreach (var duration in durations)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.BrowserSessionCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.MaximumQueued);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.HeavyChildCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(policy.ProcessOutputBufferCharacters);
        return Microsoft.Extensions.Options.Options.Create(policy);
    }
}
