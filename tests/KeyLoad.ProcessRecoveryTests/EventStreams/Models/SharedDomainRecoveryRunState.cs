using KeyLoad.Storage;

namespace KeyLoad.ProcessRecoveryTests.EventStreams;

internal sealed record SharedDomainRecoveryRunState(SharedDomainRecoveryHistory[] History, string[] Image, long Position, StoreIdentity IdentityDigest);
