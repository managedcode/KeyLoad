using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class AdmissionOwnershipTests
{
    private const string PrincipalId = "principal";
    private const string TenantId = "tenant";
    private const string RequestPath = "/commands";

    [Test]
    public async Task InvalidCommandReservationArgumentsLeaveEveryCounterUnchanged()
    {
        var governor = new CommandAdmissionGovernor(UnitAdmissionOptions.Command());
        var principal = NewPrincipal();

        Assert.ThrowsExactly<ArgumentNullException>(() => governor.Reserve(OperationKind.Batch, null!, 0, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => governor.Reserve(OperationKind.Batch, principal, -1, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => governor.Reserve(OperationKind.Batch, principal, 0, -1));
        Assert.ThrowsExactly<ArgumentException>(() => governor.Reserve(OperationKind.Batch, principal with { Id = string.Empty }, 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => governor.Reserve(OperationKind.Batch, principal with { TenantId = string.Empty }, 0, 0));
        await Assert.That(governor.Snapshot()).IsEqualTo(new(0, 0, 0, 0, 0, 0));
    }

    [Test]
    public async Task HttpAdmissionRejectsInvalidPathFramingAndPrincipalBeforeReservation()
    {
        var governor = new HttpAdmissionGovernor(UnitAdmissionOptions.Http());

        Assert.ThrowsExactly<ArgumentNullException>(() => governor.Begin(null!, 0));
        Assert.ThrowsExactly<KeyLoadException>(() => governor.Begin(RequestPath, -1));
        using var lease = governor.Begin(RequestPath, 0);
        Assert.ThrowsExactly<ArgumentNullException>(() => lease.Bind(null!));
        await Assert.That(governor.Status().VerifiedScopes).IsEqualTo(new CommandAdmissionSnapshot(0, 0, 0, 0, 0, 0));
    }

    private static PrincipalRecord NewPrincipal() => new(PrincipalId, TenantId, [], []);

}
