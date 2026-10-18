using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal sealed record SharedDomainRecoveryInput(Command<DatabaseCommand> Original, Command<DatabaseCommand>[] SeedOperations,
    Result<object>[] SeedResults, string[] SeedImage, long SeedPosition, StoreIdentity OriginalIdentityDigest, Command<DatabaseCommand>[] ResourceCreations);
