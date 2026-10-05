using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueCoordinationOptionsStartupTests
{
    private const string OneSecond = "00:00:01";
    private const string OneSecondAndOneMillisecond = "00:00:01.001";
    private const string FiveHundredMilliseconds = "00:00:00.500";
    private const string FourHundredNinetyNineMilliseconds = "00:00:00.499";
    private const string PollingProperty = nameof(DueCoordinationOptions.PollInterval);
    private const string CadenceProperty = nameof(DueCoordinationOptions.MinimumCycleCadence);

    /// <summary>Central host startup accepts the frozen one-second poll and 500ms cadence boundaries.</summary>
    [Test]
    public async Task StartupAcceptsRequiredPollingAndCadenceBoundaries()
    {
        using var host = CreateHost(new Dictionary<string, string?>
        {
            [Setting(PollingProperty)] = OneSecond,
            [Setting(CadenceProperty)] = FiveHundredMilliseconds
        });
        await host.StartAsync();
        try
        {
            var options = host.Services.GetRequiredService<IOptions<DueCoordinationOptions>>().Value;
            await Assert.That(options.PollInterval).IsEqualTo(TimeSpan.FromSeconds(1));
            await Assert.That(options.MinimumCycleCadence).IsEqualTo(TimeSpan.FromMilliseconds(500));
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>Central host startup rejects polling above one second and cadence below 500ms.</summary>
    [Test]
    [Arguments(PollingProperty, OneSecondAndOneMillisecond)]
    [Arguments(CadenceProperty, FourHundredNinetyNineMilliseconds)]
    public async Task StartupRejectsSchedulingValuesOutsideTheDueScanContract(string property, string value)
    {
        using var host = CreateHost(new Dictionary<string, string?> { [Setting(property)] = value });

        var failure = (await Assert.ThrowsExactlyAsync<OptionsValidationException>(() => host.StartAsync()))!;

        await Assert.That(failure.OptionsType).IsEqualTo(typeof(DueCoordinationOptions));
        await Assert.That(failure.Failures).Contains(DueCoordinationOptions.ValidationMessage);
    }

    private static IHost CreateHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddDueCoordinationOptions(builder.Configuration);
        return builder.Build();
    }

    private static string Setting(string property) => DueCoordinationOptions.SectionName + ":" + property;
}
