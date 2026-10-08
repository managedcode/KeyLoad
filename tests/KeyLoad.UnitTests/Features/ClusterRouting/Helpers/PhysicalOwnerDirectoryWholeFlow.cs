using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Serialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerDirectoryWholeFlow
{
    internal const string ReaderId = "owner-directory-reader";
    internal const string TenantId = "system";
    internal const int Version = 1;
    internal const long InitialRevision = 1;
    internal const long EmptyRevision = 0;
    private const int MaximumFixtureRecords = 4096;
    internal static readonly Guid RegistrationId = Guid.Parse("90705c41-9dfa-4c9a-9230-0d7a5d377f21");
    internal static readonly Guid DeniedId = Guid.Parse("49a63c94-0809-4e9d-939c-7bd425549dee");
    internal static readonly RegisteredPhysicalOwnerV1 Control = new(new(
        Guid.Parse("10213243-5465-7687-98a9-bacbdcedfe0f"),
        Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
        ["http://node1:8080", "http://node2:8080", "http://node3:8080"], InitialRevision),
        ["http://node1:8080/", "http://node2:8080/", "http://node3:8080/"]);
    internal static readonly RegisteredPhysicalOwnerV1 Destination = new(new(
        Guid.Parse("da36582e-c35f-44ec-b0a4-29043539dc5f"),
        Guid.Parse("c5d21e44-c4dc-4b21-924f-90b1521cd5e5"),
        ["http://node4:8080", "http://node5:8080", "http://node6:8080"], InitialRevision),
        ["http://node4:8080/", "http://node5:8080/", "http://node6:8080/"]);

    internal static void Bootstrap(PhysicalShardCatalogFixture fixture)
    {
        var result = fixture.Bootstrap(new(Version, EmptyRevision, Control.Owner.PhysicalShardId,
            Control.Owner.Incarnation, Control.Owner.VoterIds));
        if (result.Error is not null)
        { throw new InvalidOperationException("The actual control catalog bootstrap failed."); }
    }

    internal static RegisterPhysicalOwnerV1 Request(long revision = EmptyRevision)
        => new(Version, revision, Control, Destination);

    internal static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture,
        RegisterPhysicalOwnerV1 request, Guid id, string principal = PhysicalShardCatalogFixture.RootPrincipalId)
        => fixture.Database.CreateNativeOperation(OperationKind.RegisterPhysicalOwner, id, principal,
            fixture.Database.EvaluationClock.GetUtcNow(), NativeSerialization.Serialize(request));

    internal static string[] Image(PhysicalShardCatalogFixture fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], MaximumFixtureRecords);
        if (page.HasMore)
        { throw new InvalidOperationException("The owner directory fixture exceeded its complete image bound."); }
        return page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":" +
            Convert.ToHexString(record.Value.Span)).ToArray();
    });

    internal static byte[] DirectoryBytes(PhysicalShardCatalogFixture fixture)
        => fixture.Store.Read(view => view.ReadOwnedValue(PhysicalOwnerDirectorySerialization.Key())
            ?? throw new InvalidOperationException("The actual registered directory is missing."));

    internal static PhysicalOwnerDirectoryV1 Expected(long revision = InitialRevision)
        => new(Version, revision, Control.Owner, ImmutableArray.Create(Control, Destination));
}
