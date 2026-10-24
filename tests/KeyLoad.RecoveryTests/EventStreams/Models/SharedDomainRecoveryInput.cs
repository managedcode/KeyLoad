using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.RecoveryTests.EventStreams;

internal sealed record SharedDomainRecoveryInput(Command<DatabaseCommand> Original, Command<DatabaseCommand>[] SeedOperations,
    Result<object>[] SeedResults, string[] SeedImage, long SeedPosition, StoreIdentity OriginalIdentityDigest, Command<DatabaseCommand>[] ResourceCreations);
