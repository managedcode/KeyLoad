namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class TestDatabaseDeleteFailureTests
{
    [Test]
    public async Task NativeRootEnumerationRefusalRetainsFirstDeleteFailureAfterPermissionsRepairAndHealthyFixture()
        => await TestDatabaseDeleteFailureFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
