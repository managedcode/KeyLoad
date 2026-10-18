namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeMcpAuthoritySelectionProgram
{
    internal const string Source = """
        const mcpAuthorityArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery", localArgs[2]];
        const mcpAuthority = nativeSelection(mcpAuthorityArgs, {});
        let mcpAuthorityUnsupportedCount = 0;
        for (const badFilter of ["/*/*/McpPersistedAuthorizationTests/*", "/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery*", "/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery|/*/*/OtherCase/*", "/*/*/McpPersistedAuthorizationTests/AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery "]) {
          try { nativeSelection([mcpAuthorityArgs[0], "--KeyLoadTests:Filter="+badFilter, mcpAuthorityArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            mcpAuthorityUnsupportedCount++; continue;
          }
          throw new Error('Unsupported MCP authority filter was accepted.');
        }
        const mcpCredentialArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery", localArgs[2]];
        const mcpCredential = nativeSelection(mcpCredentialArgs, {});
        let mcpCredentialUnsupportedCount = 0;
        for (const badFilter of ["/*/*/McpCredentialLifecycleTests/*", "/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery*", "/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery|/*/*/OtherCase/*", "/*/*/McpCredentialLifecycleTests/AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery "]) {
          try { nativeSelection([mcpCredentialArgs[0], "--KeyLoadTests:Filter="+badFilter, mcpCredentialArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            mcpCredentialUnsupportedCount++; continue;
          }
          throw new Error('Unsupported MCP authority filter was accepted.');
        }
        const mcpInitialArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials", localArgs[2]];
        const mcpInitial = nativeSelection(mcpInitialArgs, {});
        let mcpInitialUnsupportedCount = 0;
        for (const badFilter of ["/*/*/McpCredentialLifecycleTests/*", "/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials*", "/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials|/*/*/OtherCase/*", "/*/*/McpCredentialLifecycleTests/AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials "]) {
          try { nativeSelection([mcpInitialArgs[0], "--KeyLoadTests:Filter="+badFilter, mcpInitialArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            mcpInitialUnsupportedCount++; continue;
          }
          throw new Error('Unsupported initial credential filter was accepted.');
        }
        """;
}
