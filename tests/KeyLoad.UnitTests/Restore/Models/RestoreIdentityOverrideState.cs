using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed record RestoreIdentityOverrideState(
    Command<DatabaseCommand> Seed, Result<object> SeedResult, byte[] SeedOutcome, StoreIdentity SourceIdentity,
    long SourceCut, string[] SourceImage, IReadOnlyDictionary<string, byte[]> Archive)
{
    internal const string Principal = "root";
    internal const string Collection = BackupIndexedDocumentDedupFixture.Collection;
    internal const string FirstId = BackupIndexedDocumentDedupFixture.FirstId;
    internal const string FirstJson = BackupIndexedDocumentDedupFixture.FirstJson;
    internal const string HealthyId = BackupIndexedDocumentDedupFixture.HealthyId;
    internal const string HealthyJson = BackupIndexedDocumentDedupFixture.HealthyJson;
    internal const string PutDocumentKind = "putDocument";
    internal const string IdentityDetail = "Catalog restore requires a distinct new incarnation and signing identity.";
    internal static readonly Guid SeedId = Guid.Parse("d83e5da8-e9c6-4af5-88fb-713348f597c3");
    internal static readonly Guid HealthyCommandId = Guid.Parse("afc29eb6-afd8-4f84-8caa-9930a15d3203");

}
