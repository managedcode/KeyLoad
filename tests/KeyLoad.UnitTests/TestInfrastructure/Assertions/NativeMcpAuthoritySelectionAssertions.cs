using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeMcpAuthoritySelectionAssertions
{
    private const string AuthorityProperty = "mcpAuthority";
    private const string CredentialProperty = "mcpCredential";
    private const string AuthorityRefusedProperty = "mcpAuthorityUnsupportedCount";
    private const string CredentialRefusedProperty = "mcpCredentialUnsupportedCount";
    private const string InitialProperty = "mcpInitial";
    private const string InitialRefusedProperty = "mcpInitialUnsupportedCount";
    private const string InitialFilter = "/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials";
    private const int ExpectedRefusals = 4;
    private const string AuthorityFilter = "/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery";
    private const string CredentialFilter = "/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery";

    internal static async Task AssertAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(AuthorityProperty), AuthorityFilter).ConfigureAwait(false);
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(CredentialProperty), CredentialFilter).ConfigureAwait(false);
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(InitialProperty), InitialFilter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(InitialRefusedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
        await Assert.That(root.GetProperty(AuthorityRefusedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
        await Assert.That(root.GetProperty(CredentialRefusedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }
}
