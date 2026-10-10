using System.Security.Cryptography;
using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal sealed class SampleChunkCorruptImage
{
    private const int FirstByte = 0;
    private const byte ChangedBit = 1;
    private readonly byte[] manifestKey;
    private readonly byte[] blockKey;
    private readonly byte[] correctionKey;
    private readonly byte[] manifest;
    private readonly byte[] block;
    private readonly byte[] correction;
    private readonly SampleChunkCanonicalFixture fixture;

    internal SampleChunkCorruptImage(SampleChunkCanonicalFixture fixture)
    {
        this.fixture = fixture;
        var partition = fixture.Owner.Partition;
        manifestKey = SampleChunkKeys.Manifest(partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.FirstGeneration);
        blockKey = SampleChunkKeys.Block(partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.FirstGeneration, FirstByte);
        correctionKey = SampleChunkKeys.Correction(partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, fixture.WindowId, SampleChunkCanonicalFixture.CorrectedSequence);
        (manifest, block, correction) = fixture.Owner.Store.Read(view =>
            (view.ReadOwnedValue(manifestKey)!, view.ReadOwnedValue(blockKey)!, view.ReadOwnedValue(correctionKey)!));
    }

    internal ErrorCode Apply(SampleChunkCorruptionCut cut)
    {
        fixture.Owner.Store.Commit((transaction, _) =>
        {
            switch (cut)
            {
                case SampleChunkCorruptionCut.MissingManifest:
                    transaction.Delete(manifestKey);
                    break;
                case SampleChunkCorruptionCut.ManifestDigest:
                    var envelope = NativeSerialization.Deserialize<SampleChunkManifestEnvelope>(manifest);
                    transaction.Put(manifestKey, NativeSerialization.Serialize(envelope with { Digest = Flip(envelope.Digest.Span) }));
                    break;
                case SampleChunkCorruptionCut.ManifestVersion:
                case SampleChunkCorruptionCut.ManifestGeneration:
                    transaction.Put(manifestKey, ChangedManifest(cut));
                    break;
                case SampleChunkCorruptionCut.MissingBlock:
                    transaction.Delete(blockKey);
                    break;
                case SampleChunkCorruptionCut.BlockDigest:
                    transaction.Put(blockKey, Flip(block));
                    break;
                case SampleChunkCorruptionCut.MissingCorrection:
                    transaction.Delete(correctionKey);
                    break;
                case SampleChunkCorruptionCut.CorrectionSequence:
                    var row = NativeSerialization.Deserialize<SampleRecord>(correction);
                    transaction.Put(correctionKey, NativeSerialization.Serialize(row with { Sequence = SampleChunkCanonicalFixture.InitialSequence }));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(cut));
            }
            return true;
        });
        return cut == SampleChunkCorruptionCut.ManifestVersion ? ErrorCode.FormatUnsupported : ErrorCode.Corruption;
    }

    internal void Restore() => fixture.Owner.Store.Commit((transaction, _) =>
    {
        transaction.Put(manifestKey, manifest);
        transaction.Put(blockKey, block);
        transaction.Put(correctionKey, correction);
        return true;
    });

    private byte[] ChangedManifest(SampleChunkCorruptionCut cut)
    {
        var envelope = NativeSerialization.Deserialize<SampleChunkManifestEnvelope>(manifest);
        var actual = NativeSerialization.Deserialize<SampleChunkManifest>(envelope.Encoded.Span);
        var malformed = cut == SampleChunkCorruptionCut.ManifestVersion
            ? actual with { Version = checked(actual.Version + ChangedBit) }
            : actual with { Generation = SampleChunkCanonicalFixture.MergedGeneration };
        var bytes = NativeSerialization.Serialize(malformed);
        return NativeSerialization.Serialize(new SampleChunkManifestEnvelope(bytes, SHA256.HashData(bytes)));
    }

    private static byte[] Flip(ReadOnlySpan<byte> original)
    {
        var changed = original.ToArray();
        changed[FirstByte] ^= ChangedBit;
        return changed;
    }
}
