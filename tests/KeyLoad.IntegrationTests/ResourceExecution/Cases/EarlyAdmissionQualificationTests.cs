namespace KeyLoad.IntegrationTests.ResourceExecution;

[NotInParallel]
internal sealed class EarlyAdmissionQualificationTests
{
    [Test]
    public Task HeldWireRefusesMalformedSqlBeforeParseThenFreshPolicyAndTwoCold()
        => EarlyAdmissionQualificationFlow.RunAsync();
}
