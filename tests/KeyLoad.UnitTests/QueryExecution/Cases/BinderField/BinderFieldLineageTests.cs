namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class BinderFieldLineageTests
{
    [Test]
    public Task SourceReadAndExpressionUseRemainIndependentAcrossAliasesEpochsAndBothColdCuts()
        => BinderFieldLineageFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task PersistedSchemaAuthorityControlsBindingMetadataAndRootSystemOperandsAcrossBothColdCuts()
        => SchemaVisibleProjectionFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
