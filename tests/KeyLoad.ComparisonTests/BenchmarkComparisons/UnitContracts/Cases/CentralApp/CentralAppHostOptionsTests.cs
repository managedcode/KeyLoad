using Aspire.Hosting;
using KeyLoad.Comparisons;
using KeyLoad.Core;
using KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.TestInfrastructure;

internal sealed class CentralAppHostOptionsTests
{
    private const string MaxSampleMetadataBytesSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.MaxSampleMetadataBytes);
    private const string MaxHardwareBytesSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.MaxHardwareBytes);
    private const string CadenceSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.Cadence);
    private const string NativeReadBufferBytesSetting = ScaleServerResourceOptions.SectionName + ":" + nameof(ScaleServerResourceOptions.NativeReadBufferBytes);
    private const string RequestsPerScopeSetting = IsolatedKeyLoadAdmissionOptions.SectionName + ":" + nameof(IsolatedKeyLoadAdmissionOptions.RequestsPerScope);

    [Test]
    public async Task BoundResourcePolicyControlsChargingAndKeepsActualEvidence()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration[MaxSampleMetadataBytesSetting] = "128";
        builder.Configuration[MaxHardwareBytesSetting] = "64";
        builder.Configuration[CadenceSetting] = "00:00:00.100";
        var runtime = LoadTestsOptionsRegistration.Get(builder);
        var budget = new ScaleServerResourceSampleBudget(runtime.ServerResources, runtime.Provenance);
        budget.Charge(128);
        await Assert.That(budget.Remaining).IsEqualTo(0);
        Assert.ThrowsExactly<InvalidDataException>(() => budget.Charge(1));
        var policy = ScaleServerObservationPolicySnapshot.Capture(runtime.ServerResources);
        await Assert.That(policy.MaxSampleMetadataBytes).IsEqualTo(128);
        await Assert.That(policy.CadenceMilliseconds).IsEqualTo(100);
        await Assert.That(ReferenceEquals(runtime, LoadTestsOptionsRegistration.Get(builder))).IsTrue();
    }

    [Test]
    public async Task InvalidPolicyRejectsBeforeAnyAspireResourcesExist()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration[NativeReadBufferBytesSetting] = "0";
        var failure = Assert.ThrowsExactly<OptionsValidationException>(() => LoadTestsOptionsRegistration.Get(builder));
        await Assert.That(failure.OptionsType).IsEqualTo(typeof(ScaleServerResourceOptions));
        await Assert.That(builder.Resources.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CentralIsolatedAdmissionControlsTheActualGovernor()
    {
        var builder = DistributedApplication.CreateBuilder(new DistributedApplicationOptions { DisableDashboard = true, Args = [] });
        builder.Configuration[RequestsPerScopeSetting] = "2";
        var runtime = LoadTestsOptionsRegistration.Get(builder);
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
        await Assert.That(ReferenceEquals(runtime, LoadTestsOptionsRegistration.Get(builder))).IsTrue();
    }

}
