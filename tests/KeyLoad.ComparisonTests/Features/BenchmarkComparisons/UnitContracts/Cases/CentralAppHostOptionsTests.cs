using Aspire.Hosting;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Hosting;
using KeyLoad.Comparisons;
using KeyLoad.Core;
using KeyLoad.Orleans;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal sealed class CentralAppHostOptionsTests
{
    private const string OrdinaryTimeoutSetting = TestExecutionOptions.SectionName + ":" + nameof(TestExecutionOptions.OrdinaryTimeout);
    private const string MaximumFilterCharactersSetting = TestExecutionOptions.SectionName + ":" + nameof(TestExecutionOptions.MaximumFilterCharacters);
    private const string MaxSampleMetadataBytesSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.MaxSampleMetadataBytes);
    private const string MaxHardwareBytesSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.MaxHardwareBytes);
    private const string CadenceSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.Cadence);
    private const string NativeReadBufferBytesSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.NativeReadBufferBytes);
    private const string RequestsPerScopeSetting = IsolatedKeyLoadAdmissionOptions.SectionName + ":" + nameof(IsolatedKeyLoadAdmissionOptions.RequestsPerScope);
    private const string ReadBarrierPerVoterSetting = IsolatedKeyLoadReplayOptionsRegistration.SectionName + ":" + nameof(ReplicaReplayLimits.ReadBarrierPerVoter);

    [Test]
    public async Task ConfiguredExecutionChangesTimeoutAndFilterAdmission()
    {
        using var configuration = new ConfigurationManager();
        configuration[TestSuiteSettings.SuiteSetting] = "unit";
        configuration[TestSuiteSettings.FilterSetting] = "abcd";
        configuration[OrdinaryTimeoutSetting] = "00:02:00";
        configuration[MaximumFilterCharactersSetting] = "4";
        var accepted = TestSuiteSettings.Read(configuration)!;
        await Assert.That(accepted.Timeout).IsEqualTo(TimeSpan.FromMinutes(2));
        configuration[MaximumFilterCharactersSetting] = "3";
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
        builder.Configuration[MaxSampleMetadataBytesSetting] = "128";
        builder.Configuration[MaxHardwareBytesSetting] = "64";
        builder.Configuration[CadenceSetting] = "00:00:00.100";
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
        builder.Configuration[NativeReadBufferBytesSetting] = "0";
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => AppHostOptionsRegistration.Get(builder));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ScaleServerResourceOptions));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CentralIsolatedAdmissionControlsTheActualGovernor()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration[RequestsPerScopeSetting] = "2";
        var runtime = AppHostOptionsRegistration.Get(builder);
        var governor = new HttpAdmissionGovernor(IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(runtime.IsolatedAdmission));
        builder.Configuration[RequestsPerScopeSetting] = "3";
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
        builder.Configuration[ReadBarrierPerVoterSetting] = "196609";
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => AppHostOptionsRegistration.Get(builder));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ReplicaReplayLimits));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
    }
}
