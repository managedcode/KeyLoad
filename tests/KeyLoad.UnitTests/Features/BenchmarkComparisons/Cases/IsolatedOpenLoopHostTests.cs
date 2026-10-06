
namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Exercises open-loop configuration through the real isolated comparison-host process.</summary>
internal sealed class IsolatedOpenLoopHostTests
{
    [Test]
    public async Task ValidScaledOpenLoopPolicyPublishesUnsupportedWorkerWithoutNativeAcquisition()
    {
        using var fixture = new IsolatedHostFixture();
        var settings = IsolatedOpenLoopHostTestSettings.Create(fixture);
        IsolatedOpenLoopHostTestSettings.AddNativeCanaries(settings);
        var exit = await IsolatedHostFixture.RunAsync(settings);
        await Assert.That(exit.ExitCode).IsEqualTo(0);
        await Assert.That(exit.Stderr).IsEqualTo(string.Empty);
        await IsolatedOpenLoopHostAssertions.AssertUnsupportedWorkerAsync(fixture, exit);
    }

    [Test]
    [Arguments("QueueCapacity", "63")]
    [Arguments("ConcurrentSessions", "15")]
    [Arguments("MaximumNodes", "2")]
    [Arguments("OperationDeadlineMilliseconds", "29999")]
    [Arguments("DrainMilliseconds", "29999")]
    [Arguments("ControlPollMilliseconds", "99")]
    [Arguments("SpinWindowMicroseconds", "199")]
    public async Task EachNonQualifiedPolicyValueFailsBeforeOutputThenCorrectedHostPublishes(
        string option, string value)
    {
        using var fixture = new IsolatedHostFixture();
        var settings = IsolatedOpenLoopHostTestSettings.Create(fixture);
        settings[IsolatedOpenLoopHostTestSettings.OptionKey(option)] = value;
        IsolatedOpenLoopHostTestSettings.AddNativeCanaries(settings);
        await IsolatedOpenLoopHostAssertions.AssertNoOutputAsync(fixture,
            await IsolatedHostFixture.RunAsync(settings));
        settings.Remove(IsolatedOpenLoopHostTestSettings.OptionKey(option));
        var retry = await IsolatedHostFixture.RunAsync(settings);
        await Assert.That(retry.ExitCode).IsEqualTo(0);
        await IsolatedOpenLoopHostAssertions.AssertUnsupportedWorkerAsync(fixture, retry);
    }

    [Test]
    public async Task CancellationProofForUnsupportedTopologyFailsBeforeOutputThenValidSelectionPublishes()
    {
        using var fixture = new IsolatedHostFixture();
        var settings = IsolatedOpenLoopHostTestSettings.Create(fixture);
        settings[IsolatedOpenLoopHostTestSettings.ProofKey] = "true";
        IsolatedOpenLoopHostTestSettings.AddNativeCanaries(settings);
        await IsolatedOpenLoopHostAssertions.AssertNoOutputAsync(fixture,
            await IsolatedHostFixture.RunAsync(settings));
        settings.Remove(IsolatedOpenLoopHostTestSettings.ProofKey);
        var retry = await IsolatedHostFixture.RunAsync(settings);
        await Assert.That(retry.ExitCode).IsEqualTo(0);
        await IsolatedOpenLoopHostAssertions.AssertUnsupportedWorkerAsync(fixture, retry);
    }
}
