namespace KeyLoad.FunctionalTests.OrleansExecution;

[RequestCqrsHostedDataSource(textMaintenance: true)]
internal sealed class RequestCqrsTextMaintenanceTests(RequestCqrsHostedRuntimeFixture fixture)
{
    [Test]
    public Task SignedMalformedTextPayloadKeepsStartedFailedThenSameConnectionHealthyReplay()
        => RequestCqrsMalformedTextFlow.RunAsync(fixture);

    [Test]
    [Arguments(TextIndexMaintenanceMode.Build)]
    [Arguments(TextIndexMaintenanceMode.Restore)]
    public Task SignedTextMaintenanceCarriesEveryPhaseAndFullOriginalReceiptThenHealthy(TextIndexMaintenanceMode mode)
        => RequestCqrsTextMaintenanceFlow.RunAsync(fixture, mode, null);

    [Test]
    [Arguments(RequestCqrsTextFault.WrongIdentity)]
    [Arguments(RequestCqrsTextFault.UnexpectedPhase)]
    [Arguments(RequestCqrsTextFault.MixedPhase)]
    [Arguments(RequestCqrsTextFault.ExtraCompleted)]
    [Arguments(RequestCqrsTextFault.AfterFinal)]
    [Arguments(RequestCqrsTextFault.FrameBudget)]
    public Task MalformedTextProgressJoinsOriginalProducerThenHealthyWithoutChangingTheCut(RequestCqrsTextFault fault)
        => RequestCqrsTextMaintenanceFlow.RunAsync(fixture, TextIndexMaintenanceMode.Build, fault);
}
