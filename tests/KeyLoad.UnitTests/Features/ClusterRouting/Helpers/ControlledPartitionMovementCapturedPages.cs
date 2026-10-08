using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Compares all native page bytes to the complete original source reference, preserving control-owned outcomes.</summary>
internal static class ControlledPartitionMovementCapturedPages
{
    private const int Version = 1;
    private const int SeparatorCharacters = 1;
    private const int HexCharactersPerByte = 2;
    private const int NoPages = 0;
    private const int OnePage = 1;
    internal static async Task AssertAsync(string[] sourceReference, PartitionMovementCaptureHandle handle,
        PartitionMovementPageResult[] results)
    {
        var pages = results.Select(result => NativeSerialization.Deserialize<PartitionMoveImagePage>(result.NativePage.Span))
            .ToArray();
        var familyNames = handle.Descriptor.Families.Select(family => family.Family).ToArray();
        await Assert.That(familyNames.SequenceEqual(PartitionRecordFamilies.All, StringComparer.Ordinal)).IsTrue();
        var eligible = new List<string>();
        var expectedFamilies = new List<PartitionMoveImageFamily>();
        foreach (var family in handle.Descriptor.Families)
        {
            var prefix = Convert.ToHexString(KeySpace.Partition(family.Family, ControlledPartitionMovementCorpus.Partition));
            var original = sourceReference.Where(row => row.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            await Assert.That(family.RecordCount).IsEqualTo((long)original.Length);
            var rawBytes = original.Sum(row => (long)(row.Length - SeparatorCharacters) / HexCharactersPerByte);
            await Assert.That(family.RawBytes).IsEqualTo(rawBytes);
            await Assert.That(family.Digest).IsEqualTo(ControlledPartitionMovementRecordDigest.Of(original));
            var selected = pages.Where(page => page.Family == family.Family).ToArray();
            var controlOwned = family.Family is PartitionRecordFamilies.OutcomeV2
                or PartitionRecordFamilies.OutcomeLocator or PartitionRecordFamilies.OutcomeLocatorV2;
            var expectedPages = controlOwned || original.Length == NoPages ? NoPages : OnePage;
            await Assert.That(selected.Length).IsEqualTo(expectedPages);
            expectedFamilies.Add(new(family.Family, original.Length, rawBytes, expectedPages,
                ControlledPartitionMovementRecordDigest.Of(original)));
            await Assert.That(family.PageCount).IsEqualTo(selected.Length);
            if (family.Family is PartitionRecordFamilies.OutcomeV2 or PartitionRecordFamilies.OutcomeLocator
                or PartitionRecordFamilies.OutcomeLocatorV2)
            { await Assert.That(selected.Length).IsEqualTo(NoPages); }
            else
            {
                var actual = selected.SelectMany(page => page.Records).Select(record =>
                    Convert.ToHexString(record.Key.Span) + ":" + Convert.ToHexString(record.Value.Span)).ToArray();
                await Assert.That(actual.SequenceEqual(original, StringComparer.Ordinal)).IsTrue();
                eligible.AddRange(original);
            }
        }
        for (var ordinal = 0; ordinal < pages.Length; ordinal++)
        {
            var page = pages[ordinal];
            await Assert.That(page.Version).IsEqualTo(Version);
            await Assert.That(page.Ordinal).IsEqualTo(ordinal);
            await Assert.That(page.MoveId).IsEqualTo(ControlledPartitionMovementPrepareRequest.MoveId);
            await Assert.That(page.Partition).IsEqualTo(ControlledPartitionMovementCorpus.Partition);
            var rows = page.Records.Select(record => Convert.ToHexString(record.Key.Span) + ":"
                + Convert.ToHexString(record.Value.Span)).ToArray();
            await Assert.That(page.Digest).IsEqualTo(ControlledPartitionMovementRecordDigest.Of(rows));
        }
        await Assert.That(pages.Sum(page => page.Records.Length)).IsEqualTo(eligible.Count);
        await Assert.That(handle.Descriptor.Digest)
            .IsEqualTo(ControlledPartitionMovementDescriptorDigest.Of(expectedFamilies));
    }
}
