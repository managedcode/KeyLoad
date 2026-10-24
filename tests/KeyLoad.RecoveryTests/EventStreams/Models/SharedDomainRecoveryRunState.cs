using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.EventStreams;

internal sealed record SharedDomainRecoveryRunState(SharedDomainRecoveryHistory[] History, string[] Image, long Position, StoreIdentity IdentityDigest);
