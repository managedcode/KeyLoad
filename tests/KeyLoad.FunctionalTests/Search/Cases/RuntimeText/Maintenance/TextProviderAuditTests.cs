namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextProviderAuditTests
{
    [Test]
    [Arguments(TextProviderAuditDamage.Tokenizer)]
    [Arguments(TextProviderAuditDamage.Hash)]
    [Arguments(TextProviderAuditDamage.SourceIncarnation)]
    [Arguments(TextProviderAuditDamage.ScopePosition)]
    [Arguments(TextProviderAuditDamage.RecordDigest)]
    [Arguments(TextProviderAuditDamage.DocumentCount)]
    [Arguments(TextProviderAuditDamage.ForeignPartition)]
    [Arguments(TextProviderAuditDamage.FileDigest)]
    public async Task ChecksumValidSemanticDamageRefusesWithoutEffectsAndExactRepairRetainsBilingualHistoryThroughCold(
        TextProviderAuditDamage damage)
        => await TextProviderAuditFlow.RunAsync(damage, TestContext.Current!.Execution.CancellationToken);
}
