namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

[NotInParallel]
internal sealed class EarlyAdmissionQualificationTests
{
    [Test]
    public Task HeldWireRefusesMalformedSqlBeforeParseThenFreshPolicyAndTwoCold()
        => EarlyAdmissionQualificationFlow.RunAsync();
}
