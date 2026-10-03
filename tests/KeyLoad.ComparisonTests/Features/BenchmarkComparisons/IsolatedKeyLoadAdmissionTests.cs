using KeyLoad.Comparisons;
using KeyLoad.Core;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedKeyLoadAdmissionTests
{
    private const string ReadPath = "/v1/documents/get";
    private const string PrincipalId = "principal";
    private const string TenantId = "tenant";
    /// <summary>AC-ISO-003/005: the benchmark accommodates c16 while production remains bounded at eight.</summary>
    [Test]
    public async Task IntensiveAdmissionAcceptsSixteenVerifiedClientsAndRejectsCapacityOverflow()
    {
        var limits = IsolatedKeyLoadAdmissionProfile.Limits;
        var governor = new HttpAdmissionGovernor(limits);
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
        var limits = IsolatedKeyLoadAdmissionProfile.Limits;
        var status = new HttpAdmissionGovernor(limits).Status();
        IsolatedKeyLoadAdmissionProfile.Verify(status);
        await Assert.That(Assert.ThrowsExactly<ComparisonFailureException>(
            () => IsolatedKeyLoadAdmissionProfile.Verify(null))).IsNotNull();
        await Assert.That(Assert.ThrowsExactly<ComparisonFailureException>(
            () => IsolatedKeyLoadAdmissionProfile.Verify(new HttpAdmissionGovernor().Status()))).IsNotNull();
        await Assert.That(Assert.ThrowsExactly<ComparisonFailureException>(
            () => IsolatedKeyLoadAdmissionProfile.Verify(status with
            { Limits = limits with { MaxBodyBytes = 1_024 } }))).IsNotNull();
    }
}
