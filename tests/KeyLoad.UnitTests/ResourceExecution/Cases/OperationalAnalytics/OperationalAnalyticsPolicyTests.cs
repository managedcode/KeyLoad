using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class OperationalAnalyticsPolicyTests
{
    private const string PeriodicSampleSetting = "KeyLoad:OperationalAnalytics:SampleInterval";

    [Test]
    public Task InvalidTypedPolicyAcquisitionPreservesCanonicalThenHealthySamplingSurvivesTwoJoinedColdReopens()
        => OperationalAnalyticsPolicyFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public void FormerPeriodicSampleSettingIsRejectedBeforeNativeAcquisition()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [PeriodicSampleSetting] = "00:00:10"
        }).Build();
        var services = new ServiceCollection();
        services.AddRuntimeOptions(configuration);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OperationalAnalyticsOptions>>();
        Assert.ThrowsExactly<InvalidOperationException>(() => _ = options.Value);
    }
}
