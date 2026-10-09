using KeyLoad.Comparisons;
using KeyLoad.Core;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKeyLoadAdmissionTests
{
    private const string ReadPath = "/v1/documents/get";
    private const string PrincipalId = "principal";
    private const string TenantId = "tenant";
    private const int ChangedBodyBytes = 131_072;
    /// <summary>AC-ISO-003/005: the benchmark accommodates c16 while production remains bounded at eight.</summary>
    [Test]
    public async Task IntensiveAdmissionAcceptsSixteenVerifiedClientsAndRejectsCapacityOverflow()
    {
        var options = IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(NativeExecutionPolicyFixture.Admission());
        var limits = options.Value;
        var governor = new HttpAdmissionGovernor(options);
        var leases = new List<HttpAdmissionLease>();
        try
        {
            for (var index = 0; index < limits.MaxRequests; index++)
            {
                var lease = governor.Begin(ReadPath, 0);
                leases.Add(lease);
                lease.Bind(new(PrincipalId, TenantId, [], []));
            }
            await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(32);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(
                () => governor.Begin(ReadPath, 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        finally
        {
            foreach (var lease in leases)
            {
                lease.Dispose();
            }
        }
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(0);
        await Assert.That(governor.Status().VerifiedScopes.ActivePrincipalScopes).IsEqualTo(0);
        await Assert.That(new HttpAdmissionLimits().MaxPrincipalRequests).IsEqualTo(8);
        await Assert.That(limits.MaxBodyBytes).IsEqualTo(new HttpAdmissionLimits().MaxBodyBytes);
        await Assert.That(limits.HeavyReadReservedBytes).IsEqualTo(new HttpAdmissionLimits().HeavyReadReservedBytes);
    }

    /// <summary>AC-ISO-003/005: missing, default and changed native SDK limits fail the declared profile.</summary>
    [Test]
    public async Task ActualMemberAdmissionMustMatchEveryDeclaredLimit()
    {
        var options = IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(NativeExecutionPolicyFixture.Admission());
        var limits = options.Value;
        var status = new HttpAdmissionGovernor(options).Status();
        IsolatedKeyLoadAdmissionProfile.Verify(status, options);
        await Assert.That(Assert.ThrowsExactly<ComparisonFailureException>(
            () => IsolatedKeyLoadAdmissionProfile.Verify(null, options))).IsNotNull();
        await Assert.That(Assert.ThrowsExactly<ComparisonFailureException>(
            () => IsolatedKeyLoadAdmissionProfile.Verify(new HttpAdmissionGovernor(NativeExecutionPolicyFixture.Http()).Status(), options))).IsNotNull();
        await Assert.That(Assert.ThrowsExactly<ComparisonFailureException>(
            () => IsolatedKeyLoadAdmissionProfile.Verify(
                new HttpAdmissionGovernor(NativeExecutionPolicyFixture.Http(limits with { MaxBodyBytes = ChangedBodyBytes })).Status(), options))).IsNotNull();
    }

    [Test]
    public async Task ConfiguredSlotsReachActualAdmissionAndReleaseOnDisposal()
    {
        var options = IsolatedKeyLoadAdmissionOptions.CreateHttpOptions(NativeExecutionPolicyFixture.Admission(
            new IsolatedKeyLoadAdmissionOptions { RequestsPerScope = 2 }));
        var governor = new HttpAdmissionGovernor(options);
        using (var first = governor.Begin(ReadPath, 0))
        using (var second = governor.Begin(ReadPath, 0))
        {
            first.Bind(new(PrincipalId, TenantId, [], []));
            second.Bind(new(PrincipalId, TenantId, [], []));
            IsolatedKeyLoadAdmissionProfile.Verify(governor.Status(), options);
            await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(2);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(
                () => governor.Begin(ReadPath, 0)).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        await Assert.That(governor.Status().Node.Commands).IsEqualTo(0);
        using var replacement = governor.Begin(ReadPath, 0);
        replacement.Bind(new(PrincipalId, TenantId, [], []));
        await Assert.That(governor.Status().VerifiedScopes.Commands).IsEqualTo(1);
    }
}
