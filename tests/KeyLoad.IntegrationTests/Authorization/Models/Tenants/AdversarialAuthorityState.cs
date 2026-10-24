using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Authorization;

internal sealed record AdversarialAuthorityState(AdversarialAuthoritySeed Owned,
    AdversarialAuthoritySeed Foreign, McpPersistedIdentity Identity);
