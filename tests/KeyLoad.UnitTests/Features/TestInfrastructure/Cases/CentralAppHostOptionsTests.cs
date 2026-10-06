using Aspire.Hosting;
using KeyLoad.Comparisons;
using KeyLoad.Core;
using KeyLoad.Orleans;
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

    [Test]
    public async Task CentralIsolatedAdmissionControlsTheActualGovernor()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration["IsolatedKeyLoadAdmission:RequestsPerScope"] = "2";
        var runtime = AppHostOptionsRegistration.Get(builder);
        var governor = new HttpAdmissionGovernor(IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(runtime.IsolatedAdmission));
        builder.Configuration["IsolatedKeyLoadAdmission:RequestsPerScope"] = "3";
        using (var first = governor.Begin("/v1/documents/get", 0))
        using (var second = governor.Begin("/v1/documents/get", 0))
        {
            first.Bind(new("principal", "tenant", [], []));
            second.Bind(new("principal", "tenant", [], []));
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(
                () => governor.Begin("/v1/documents/get", 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
            await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(2);
        }
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(0);
        await Assert.That(ReferenceEquals(runtime, AppHostOptionsRegistration.Get(builder))).IsTrue();
    }

    [Test]
    public async Task InvalidIsolatedReplayRejectsBeforeContainerComposition()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration["Benchmarks:IsolatedReplayAdmission:ReadBarrierPerVoter"] = "196609";
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => AppHostOptionsRegistration.Get(builder));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ReplicaReplayLimits));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
    }
}
