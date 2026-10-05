using KeyLoad.Orleans;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class DueCoordinationOptionsTests
{
    [Test]
    public async Task CentralBindingPublishesConfiguredNativeOptionsAfterStartupValidation()
    {
        using var host = CreateHost(new Dictionary<string, string?>
        {
            [Setting(nameof(DueCoordinationOptions.DispatchDeadline))] = "00:00:07",
            [Setting(nameof(DueCoordinationOptions.PollInterval))] = "00:00:00.500",
            [Setting(nameof(DueCoordinationOptions.MinimumCycleCadence))] = "00:00:00.750",
            [Setting(nameof(DueCoordinationOptions.UncertaintyRetryCount))] = "0"
        });
        await host.StartAsync();
        try
        {
            var configured = host.Services.GetRequiredService<IOptions<DueCoordinationOptions>>();
            await Assert.That(configured.Value.DispatchDeadline).IsEqualTo(TimeSpan.FromSeconds(7));
            await Assert.That(configured.Value.PollInterval).IsEqualTo(TimeSpan.FromMilliseconds(500));
            await Assert.That(configured.Value.MinimumCycleCadence).IsEqualTo(TimeSpan.FromMilliseconds(750));
            await Assert.That(configured.Value.UncertaintyRetryCount).IsEqualTo(0);
            await Assert.That(ReferenceEquals(configured,
                host.Services.GetRequiredService<IOptions<DueCoordinationOptions>>())).IsTrue();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Test]
    [Arguments(nameof(DueCoordinationOptions.DispatchDeadline), "00:00:00")]
    [Arguments(nameof(DueCoordinationOptions.DispatchDeadline), "-00:00:01")]
    [Arguments(nameof(DueCoordinationOptions.DispatchDeadline), "00:01:00.001")]
    [Arguments(nameof(DueCoordinationOptions.PollInterval), "00:00:00")]
    [Arguments(nameof(DueCoordinationOptions.PollInterval), "00:00:01.001")]
    [Arguments(nameof(DueCoordinationOptions.PollInterval), "00:01:00.001")]
    [Arguments(nameof(DueCoordinationOptions.MinimumCycleCadence), "00:00:00")]
    [Arguments(nameof(DueCoordinationOptions.MinimumCycleCadence), "00:00:00.499")]
    [Arguments(nameof(DueCoordinationOptions.MinimumCycleCadence), "00:01:00.001")]
    [Arguments(nameof(DueCoordinationOptions.UncertaintyRetryCount), "-1")]
    [Arguments(nameof(DueCoordinationOptions.UncertaintyRetryCount), "2")]
    public async Task InvalidConfigurationRejectsHostStartup(string property, string value)
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
