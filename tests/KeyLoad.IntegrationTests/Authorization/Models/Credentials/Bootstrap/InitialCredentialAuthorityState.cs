using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record InitialCredentialAuthorityState(AdversarialAuthoritySeed Seed, McpPersistedIdentity Identity);
