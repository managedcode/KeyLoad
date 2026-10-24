namespace KeyLoad.UnitTests.Search;

internal sealed class TextProviderAuditTests
{
    [Test]
    [Arguments(TextProviderAuditDamage.Tokenizer)]
    [Arguments(TextProviderAuditDamage.Hash)]
    [Arguments(TextProviderAuditDamage.Format)]
    [Arguments(TextProviderAuditDamage.ScopePosition)]
    [Arguments(TextProviderAuditDamage.RecordId)]
    [Arguments(TextProviderAuditDamage.DuplicateReference)]
    [Arguments(TextProviderAuditDamage.ForeignPartition)]
    [Arguments(TextProviderAuditDamage.FileDigest)]
    public async Task ChecksumValidSemanticDamageRefusesWithoutEffectsAndExactRepairRetainsBilingualHistoryThroughCold(
        TextProviderAuditDamage damage)
        => await TextProviderAuditFlow.RunAsync(damage, TestContext.Current!.Execution.CancellationToken);
}
