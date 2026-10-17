namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class FullPartitionOutcomeIdentityTests
{
    [Test]
    public Task AcDstore009TenantIdParticipatesInPartitionOutcomeIdentity()
        => FullPartitionOutcomeIdentityScenario.RunTenantAsync();

    [Test]
    public Task AcDstore009DatabaseIdParticipatesInPartitionOutcomeIdentity()
        => FullPartitionOutcomeIdentityScenario.RunDatabaseAsync();

    [Test]
    public Task AcDstore009TransactionDomainIdParticipatesInPartitionOutcomeIdentity()
        => FullPartitionOutcomeIdentityScenario.RunTransactionDomainAsync();

    [Test]
    public Task AcDstore009PartitionKeyParticipatesInPartitionOutcomeIdentity()
        => FullPartitionOutcomeIdentityScenario.RunPartitionKeyAsync();
}
