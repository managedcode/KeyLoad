using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed record SharedDomainRecoveryRunState(SharedDomainRecoveryHistory[] History, string[] Image, long Position, StoreIdentity IdentityDigest);
