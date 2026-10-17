namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

[NotInParallel]
internal sealed class FollowerDocumentRf3Tests
{
    [Test]
    [Arguments(FollowerDocumentCaller.Sdk)]
    [Arguments(FollowerDocumentCaller.OfficialMcp)]
    [Arguments(FollowerDocumentCaller.SqlSdk)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp)]
    public Task HeldOldDocumentRetainsLiteralCutThenHealthyNativeMinimumAcrossEveryTransport(FollowerDocumentCaller caller)
        => FollowerDocumentRf3Scenario.RunAsync(caller, FollowerDocumentChange.Document,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(FollowerDocumentCaller.Sdk, FollowerDocumentChange.Credential)]
    [Arguments(FollowerDocumentCaller.OfficialMcp, FollowerDocumentChange.Credential)]
    [Arguments(FollowerDocumentCaller.SqlSdk, FollowerDocumentChange.Credential)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp, FollowerDocumentChange.Credential)]
    [Arguments(FollowerDocumentCaller.Sdk, FollowerDocumentChange.Grant)]
    [Arguments(FollowerDocumentCaller.OfficialMcp, FollowerDocumentChange.Grant)]
    [Arguments(FollowerDocumentCaller.SqlSdk, FollowerDocumentChange.Grant)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp, FollowerDocumentChange.Grant)]
    public Task HeldPrivateDocumentRechecksPersistedAuthorityThenRestoredCallerResumes(FollowerDocumentCaller caller,
        FollowerDocumentChange change)
        => FollowerDocumentRf3Scenario.RunAsync(caller, change, TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(FollowerDocumentCaller.Sdk)]
    [Arguments(FollowerDocumentCaller.OfficialMcp)]
    [Arguments(FollowerDocumentCaller.SqlSdk)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp)]
    public Task HeldDocumentUsesChangedFieldPolicyThenRestoredFieldGrantResumes(FollowerDocumentCaller caller)
        => FollowerDocumentRf3Scenario.RunAsync(caller, FollowerDocumentChange.FieldPolicy,
            TestContext.Current!.Execution.CancellationToken);

    [Test]
    [Arguments(FollowerDocumentCaller.Sdk, FollowerDocumentChange.Lag)]
    [Arguments(FollowerDocumentCaller.OfficialMcp, FollowerDocumentChange.Lag)]
    [Arguments(FollowerDocumentCaller.SqlSdk, FollowerDocumentChange.Lag)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp, FollowerDocumentChange.Lag)]
    [Arguments(FollowerDocumentCaller.Sdk, FollowerDocumentChange.Cancellation)]
    [Arguments(FollowerDocumentCaller.OfficialMcp, FollowerDocumentChange.Cancellation)]
    [Arguments(FollowerDocumentCaller.SqlSdk, FollowerDocumentChange.Cancellation)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp, FollowerDocumentChange.Cancellation)]
    public Task HeldReadRefusesExcessLagOrJoinsCancellationBeforeHealthyResume(FollowerDocumentCaller caller,
        FollowerDocumentChange change)
        => FollowerDocumentRf3Scenario.RunAsync(caller, change, TestContext.Current!.Execution.CancellationToken);
    [Test]
    [Arguments(FollowerDocumentCaller.Sdk)]
    [Arguments(FollowerDocumentCaller.OfficialMcp)]
    [Arguments(FollowerDocumentCaller.SqlSdk)]
    [Arguments(FollowerDocumentCaller.SqlOfficialMcp)]
    public Task CapturedDataCannotBypassActualLostQuorumAndRestoredVotersResume(FollowerDocumentCaller caller)
        => FollowerDocumentRf3Scenario.RunAsync(caller, FollowerDocumentChange.NoQuorum,
            TestContext.Current!.Execution.CancellationToken);

}
