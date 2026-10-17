using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal sealed record AdversarialAuthorityState(AdversarialAuthoritySeed Owned,
    AdversarialAuthoritySeed Foreign, McpPersistedIdentity Identity);
