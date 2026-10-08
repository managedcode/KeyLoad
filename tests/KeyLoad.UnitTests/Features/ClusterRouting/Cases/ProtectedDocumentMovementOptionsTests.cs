using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.AppHost.Features.ClusterRouting;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class ProtectedDocumentMovementOptionsTests
{
    [Test]
    public async Task ActualTypedSelectorEnablesAllSixNativeRuntimesAndOmissionKeepsThemDisabled()
        => await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            foreach (var enabled in new[] { false, true })
            {
                var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
                builder.Configuration.AddInMemoryCollection(Settings(enabled));
                var resources = TwoRf3ClusterResources.Add(builder, Profile(), fixture.DataRoot);
                await Assert.That(resources.Length).IsEqualTo(6);
                foreach (var resource in resources)
                {
                    var emitted = await ExecutionConfigurationBuilder.Create(resource.Resource)
                        .WithEnvironmentVariablesConfig().BuildAsync(
                            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
                            NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
                    if (emitted.Exception is { } failure)
                    { throw failure; }
                    using var actual = new ConfigurationManager();
                    actual.AddInMemoryCollection(emitted.EnvironmentVariables.Select(pair => new KeyValuePair<string, string?>(
                        pair.Key.Replace("__", ":", StringComparison.Ordinal), pair.Value)));
                    var options = actual.GetSection(PartitionMovementExecutionOptions.SectionName)
                        .Get<PartitionMovementExecutionOptions>() ?? new();
                    await Assert.That(options.Enabled).IsEqualTo(enabled);
                }
            }
        });

    [Test]
    public async Task InvalidNativeSelectorsRejectBeforeAHealthyValidatedFollowup()
    {
        foreach (var failure in new[] { "missing-query", "nested", "wrong-value", "missing-profile" })
        {
            var values = Settings(true);
            switch (failure)
            {
                case "missing-query":
                    values.Remove(TwoRf3ProfileProtocol.RemoteQuerySetting);
                    break;
                case "nested":
                    values.Add(TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting + ":extra", "true");
                    break;
                case "wrong-value":
                    values[TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting] = "TRUE";
                    break;
                case "missing-profile":
                    values.Remove(TwoRf3ProfileProtocol.Setting);
                    break;
            }
            using var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(values);
            Exception? rejected = null;
            try
            { _ = TwoRf3Profile.ValidateAndRead(configuration); }
            catch (InvalidOperationException error) { rejected = error; }
            await Assert.That(rejected?.Message).IsEqualTo(TwoRf3ProfileProtocol.Invalid);
            using var healthy = new ConfigurationManager();
            healthy.AddInMemoryCollection(Settings(true));
            await Assert.That(TwoRf3Profile.ValidateAndRead(healthy)).IsTrue();
        }
    }

    [Test]
    public async Task InvalidProtectedLifetimesRejectNativeCompositionBeforeHealthySixPeerFollowup()
        => await RequestCqrsProbeAppHostFileFixture.WithFixtureAsync(async fixture =>
        {
            foreach (var (name, value) in new[]
            {
                ("ProtectedMovementSetupRequestLifetime", "00:00:00"),
                ("ProtectedMovementSetupRequestLifetime", "00:00:51"),
                ("ProtectedMovementOutcomeRequestLifetime", "00:00:00"),
                ("ProtectedMovementOutcomeRequestLifetime", "00:00:26"),
                ("ProtectedMovementCaptureCleanupTimeout", "00:00:00"),
                ("ProtectedMovementCaptureCleanupTimeout", "00:00:31")
            })
            {
                var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
                { DisableDashboard = true, Args = [] });
                var values = Settings(true);
                values.Add(KeyLoad.AppHost.Features.TestInfrastructure.TestExecutionOptions.SectionName + ":" + name, value);
                builder.Configuration.AddInMemoryCollection(values);
                var rejected = await Assert.ThrowsAsync<Microsoft.Extensions.Options.OptionsValidationException>(() =>
                    Task.FromResult(TwoRf3ClusterResources.Add(builder, Profile(), fixture.DataRoot)));
                await Assert.That(rejected!.OptionsType).IsEqualTo(typeof(KeyLoad.AppHost.Features.TestInfrastructure.TestExecutionOptions));
                var healthy = DistributedApplication.CreateBuilder(new DistributedApplicationOptions
                { DisableDashboard = true, Args = [] });
                healthy.Configuration.AddInMemoryCollection(Settings(true));
                var resources = TwoRf3ClusterResources.Add(healthy, Profile(), fixture.DataRoot);
                await Assert.That(resources.Length).IsEqualTo(6);
                foreach (var resource in resources)
                {
                    var emitted = await ExecutionConfigurationBuilder.Create(resource.Resource)
                        .WithEnvironmentVariablesConfig().BuildAsync(
                            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
                            NullLogger.Instance, TestContext.Current!.Execution.CancellationToken);
                    await Assert.That(emitted.Exception).IsNull();
                }
            }
        });

    private static Dictionary<string, string?> Settings(bool enabled)
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [TwoRf3ProfileProtocol.Setting] = TwoRf3ProfileProtocol.Profile,
            [TwoRf3ProfileProtocol.EphemeralSetting] = "true",
            [TwoRf3ProfileProtocol.RegistrationSetting] = "true",
            [TwoRf3ProfileProtocol.RemoteDocumentSetting] = "true",
            [TwoRf3ProfileProtocol.RemoteQuerySetting] = "true",
            [RuntimeContainerImage.ServerConfiguration] = RequestCqrsProbeAppHostBuilder.ValidImage
        };
        if (enabled)
        { settings.Add(TwoRf3ProfileProtocol.ProtectedDocumentMovementSetting, "true"); }
        return settings;
    }

    private static LocalProfile Profile()
        => new(ClusterProfileStore.CurrentVersion, Guid.NewGuid(), Guid.NewGuid(), Secret(), Secret(), "root." + Guid.NewGuid().ToString("N"));

    private static string Secret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try
        { return Convert.ToBase64String(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
