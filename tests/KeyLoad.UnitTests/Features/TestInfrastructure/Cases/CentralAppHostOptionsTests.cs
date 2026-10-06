using Aspire.Hosting;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class CentralAppHostOptionsTests
{
    [Test]
    public async Task ConfiguredExecutionChangesTimeoutAndFilterAdmission()
    {
        using var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = "unit";
        configuration[TestSuiteSettings.FilterSetting] = "abcd";
        configuration["KeyLoadTests:Execution:OrdinaryTimeout"] = "00:02:00";
        configuration["KeyLoadTests:Execution:MaximumFilterCharacters"] = "4";
        var accepted = TestSuiteSettings.Read(configuration)!;
        await Assert.That(accepted.Timeout).IsEqualTo(TimeSpan.FromMinutes(2));
        configuration["KeyLoadTests:Execution:MaximumFilterCharacters"] = "3";
        Assert.ThrowsExactly<InvalidOperationException>(() => TestSuiteSettings.Read(configuration));
    }

    [Test]
    public async Task BootstrapSelectorsUseNativeOptionsBeforeDashboardComposition()
    {
        using var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = "unit";
        var options = AppHostOptionsRegistration.BindTestBootstrap(configuration);
        configuration[TestSuiteSettings.SuiteSetting] = null;
        await Assert.That(TestSuiteSettings.Requested([], options)).IsTrue();
        await Assert.That(TestSuiteSettings.Requested([], AppHostOptionsRegistration.BindTestBootstrap(configuration))).IsFalse();
    }

    [Test]
    public async Task BoundResourcePolicyControlsChargingAndKeepsActualEvidence()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration["Benchmarks:ServerResources:MaxSampleMetadataBytes"] = "128";
        builder.Configuration["Benchmarks:ServerResources:MaxHardwareBytes"] = "64";
        builder.Configuration["Benchmarks:ServerResources:Cadence"] = "00:00:00.100";
        var runtime = AppHostOptionsRegistration.Get(builder);
        var budget = new ScaleServerResourceSampleBudget(runtime.ServerResources, runtime.Provenance);
        budget.Charge(128);
        await Assert.That(budget.Remaining).IsEqualTo(0);
        Assert.ThrowsExactly<InvalidDataException>(() => budget.Charge(1));
        var policy = ScaleServerObservationPolicySnapshot.Capture(runtime.ServerResources);
        await Assert.That(policy.MaxSampleMetadataBytes).IsEqualTo(128);
        await Assert.That(policy.CadenceMilliseconds).IsEqualTo(100);
        await Assert.That(ReferenceEquals(runtime, AppHostOptionsRegistration.Get(builder))).IsTrue();
    }

    [Test]
    public async Task InvalidPolicyRejectsBeforeAnyAspireResourcesExist()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration["Benchmarks:ServerResources:NativeReadBufferBytes"] = "0";
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => AppHostOptionsRegistration.Get(builder));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ScaleServerResourceOptions));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
    }
}
