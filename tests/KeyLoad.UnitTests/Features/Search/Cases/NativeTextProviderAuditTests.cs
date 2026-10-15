namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextProviderAuditTests
{
    [Test]
    [Arguments(NativeTextProviderAuditDamage.Tokenizer)]
    [Arguments(NativeTextProviderAuditDamage.Hash)]
    [Arguments(NativeTextProviderAuditDamage.Format)]
    [Arguments(NativeTextProviderAuditDamage.ScopePosition)]
    [Arguments(NativeTextProviderAuditDamage.RecordId)]
    [Arguments(NativeTextProviderAuditDamage.DuplicateReference)]
    [Arguments(NativeTextProviderAuditDamage.ForeignPartition)]
    [Arguments(NativeTextProviderAuditDamage.FileDigest)]
    public async Task ChecksumValidSemanticDamageRefusesWithoutEffectsAndExactRepairRetainsBilingualHistoryThroughCold(
        NativeTextProviderAuditDamage damage)
        => await NativeTextProviderAuditFlow.RunAsync(damage, TestContext.Current!.Execution.CancellationToken);
}
