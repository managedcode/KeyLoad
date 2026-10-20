using System.Text.Json;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class JournalAuthorityTestBootstrap
{
    private const string JournalName = "authorization-journal";
    private const string JournalInstanceIdText = "08344d4f-c81d-41b4-b266-a6a80f52ad12";
    private static readonly Guid JournalInstanceId = Guid.Parse(JournalInstanceIdText);
    private static readonly DateTimeOffset OperationTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    internal static RuntimeJournalMutation BootstrapMutation() => new(RuntimeJournalAction.BootstrapIdentity,
        string.Empty, Guid.Empty, 0, 0, null, ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);

    internal static RuntimeJournalMutation CreateMutation() => new(RuntimeJournalAction.Create,
        JournalName, JournalInstanceId, 0, 0, null,
        ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);

    internal static void Initialize(DatabaseEngine database)
    {
        database.ConfigureRuntimeJournal(Options.Create(new RuntimeJournalOptions()));
        Verify(database);
    }

    internal static RuntimeJournalMutationResult Verify(DatabaseEngine database)
        => database.Apply(database.CreateCommand(OperationKind.RuntimeJournal, Guid.NewGuid(), PartitionStoreProtocol.AdministratorId, OperationTime, JsonSerializer.Serialize(BootstrapMutation(), JsonDefaults.Options)))
            .Get<RuntimeJournalMutationResult>();
}
