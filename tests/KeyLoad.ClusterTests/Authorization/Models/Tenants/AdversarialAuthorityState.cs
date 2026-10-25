using KeyLoad.ClusterTests.ClientApi;

namespace KeyLoad.ClusterTests.Authorization;

internal sealed record AdversarialAuthorityState(AdversarialAuthoritySeed Owned,
    AdversarialAuthoritySeed Foreign, McpPersistedIdentity Identity);
