namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class BinderFieldLineageTests
{
    [Test]
    public Task SourceReadAndExpressionUseRemainIndependentAcrossAliasesEpochsAndBothColdCuts()
        => BinderFieldLineageFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);

    [Test]
    public Task PersistedSchemaAuthorityControlsBindingMetadataAndRootSystemOperandsAcrossBothColdCuts()
        => SchemaVisibleProjectionNativeFlow.RunAsync(TestContext.Current!.Execution.CancellationToken);
}
