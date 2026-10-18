using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record RestoreIdentityOverrideCurrent(
    Command<DatabaseCommand> Operation, Result<object> Result, Result<object> Fence, byte[] Outcome, long Position, string[] Image);
