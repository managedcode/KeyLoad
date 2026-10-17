using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record InitialCredentialAuthorityState(AdversarialAuthoritySeed Seed, McpPersistedIdentity Identity);
