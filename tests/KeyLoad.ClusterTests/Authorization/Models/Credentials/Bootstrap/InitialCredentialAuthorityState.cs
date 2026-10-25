using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record InitialCredentialAuthorityState(AdversarialAuthoritySeed Seed, McpPersistedIdentity Identity);
