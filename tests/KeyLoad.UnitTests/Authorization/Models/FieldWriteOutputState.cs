using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.Authorization.FieldWriteOutputProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class FieldWriteOutputState(TestDatabase owner)
{
    internal TestDatabase Owner { get; } = owner;
    internal ZoneTreeStore Store { get; set; } = owner.Store;
    internal DatabaseEngine Database { get; set; } = owner.Database;
    internal CommandRequest Original { get; set; } = null!;
    internal CommitReceipt OriginalReceipt { get; set; } = null!;
    internal CommandRequest Denied { get; set; } = null!;
    internal CommandRequest Healthy { get; set; } = null!;
    internal CommitReceipt HealthyReceipt { get; set; } = null!;
    internal CommandRequest ReaderDenied { get; set; } = null!;
    internal Dictionary<Guid, byte[]> Outcomes { get; } = [];
    internal ResourceDefinition Resource { get; set; } = null!;
    internal EntityRef Reference => new(Owner.Partition, P.Collection, P.Document);
    internal Result<object> Apply(CommandRequest command, string principal, CancellationToken token)
        => Database.ApplyEmbedded(Database.CreateCommand(OperationKind.Batch, command.CommandId, principal, default, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options)), token);
    internal byte[] Outcome(CommandRequest command, string principal)
        => Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Owner.Partition, principal, command.CommandId)))
            ?? throw new InvalidOperationException("The original field operation outcome is absent.");
}
