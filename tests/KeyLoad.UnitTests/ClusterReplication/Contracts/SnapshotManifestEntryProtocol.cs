using KeyLoad.CrashHost.Features.ClusterReplication;

namespace KeyLoad.UnitTests.Features.ClusterReplication;

internal static class SnapshotManifestEntryProtocol
{
    internal const string Prefix = "keyload-snapshot-manifest-entry-";
    internal const string RetainedEntry = "retained-snapshot-entry";
    internal const string Manifest = SnapshotManifestEntryProbe.Manifest;
    internal const string Image = SnapshotManifestEntryProbe.Image;
    internal const string HealthyJson = "{\"value\":\"healthy\"}";
    internal const string Put = "putDocument";
    internal const string Collection = "orders";
    internal const string Document = "document";
    internal const string Dotnet = "dotnet";
    internal const string GuidFormat = "D";
    internal const int OriginalCut = 3;
    internal const int SnapshotCut = 4;
    internal const int TailCut = 5;
    internal const int HealthyCut = 6;
    internal const int Term = 1;
    internal const int OwnershipEpoch = 1;
    internal const int RevisionOffset = 1;
    internal const int FirstDocument = 2;
    internal const int ColdCount = 2;
    internal const int RecordLimit = 512;
}
