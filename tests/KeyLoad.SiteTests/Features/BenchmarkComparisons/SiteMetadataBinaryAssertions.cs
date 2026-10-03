using System.Buffers.Binary;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteMetadataBinaryAssertions
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    internal static async Task AssertPngDimensions(string path, int dimension, CancellationToken token)
        => await AssertPngDimensions(path, dimension, dimension, token);

    internal static async Task AssertPngDimensions(string path, int width, int height, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(path, token);
        await Assert.That(bytes.Length >= SiteMetadataTokens.PngHeaderLength).IsTrue();
        await Assert.That(bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)).IsTrue();
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(SiteMetadataTokens.PngIhdrOffset,
            SiteMetadataTokens.PngDimensionBytes))).IsEqualTo(width);
        await Assert.That(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(SiteMetadataTokens.PngIhdrOffset +
            SiteMetadataTokens.PngDimensionBytes, SiteMetadataTokens.PngDimensionBytes))).IsEqualTo(height);
    }

    internal static async Task AssertIcoFrames(string path, CancellationToken token)
    {
        var bytes = await File.ReadAllBytesAsync(path, token);
        await Assert.That(bytes.Length >= SiteMetadataTokens.IcoHeaderLength +
            SiteMetadataTokens.IcoEntryLength * SiteMetadataTokens.IcoFrameCount).IsTrue();
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2))).IsEqualTo((ushort)0);
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2))).IsEqualTo((ushort)1);
        await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2)))
            .IsEqualTo((ushort)SiteMetadataTokens.IcoFrameCount);
        var dimensions = new[] { SiteMetadataTokens.IcoDimension16, SiteMetadataTokens.IcoDimension32,
            SiteMetadataTokens.IcoDimension48 };
        for (var index = 0; index < dimensions.Length; index++)
        {
            var entry = bytes.AsMemory(SiteMetadataTokens.IcoHeaderLength + index * SiteMetadataTokens.IcoEntryLength,
                SiteMetadataTokens.IcoEntryLength).ToArray();
            var dimension = entry[SiteMetadataTokens.IcoWidthOffset] == SiteMetadataTokens.IcoZeroDimensionMeans256
                ? SiteMetadataTokens.IcoDimension256 : entry[SiteMetadataTokens.IcoWidthOffset];
            await Assert.That(dimension).IsEqualTo(dimensions[index]);
            await Assert.That(entry[SiteMetadataTokens.IcoHeightOffset]).IsEqualTo((byte)dimensions[index]);
            await Assert.That(entry[SiteMetadataTokens.IcoTypeOffset]).IsEqualTo((byte)SiteMetadataTokens.IcoTypeValue);
            await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(SiteMetadataTokens.IcoEntryPlanesOffset, 2)))
                .IsEqualTo((ushort)SiteMetadataTokens.IcoPlanes);
            await Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(entry.AsSpan(SiteMetadataTokens.IcoEntryBitsOffset, 2)))
                .IsEqualTo((ushort)SiteMetadataTokens.IcoBitsPerPixel);
            var frameLength = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(SiteMetadataTokens.IcoEntryFrameSizeOffset, 4));
            var frameOffset = BinaryPrimitives.ReadUInt32LittleEndian(entry.AsSpan(SiteMetadataTokens.IcoEntryImageOffset, 4));
            await Assert.That(frameOffset >= SiteMetadataTokens.IcoHeaderLength +
                SiteMetadataTokens.IcoEntryLength * SiteMetadataTokens.IcoFrameCount).IsTrue();
            await Assert.That(frameOffset + frameLength <= bytes.Length).IsTrue();
            var frame = bytes.AsMemory((int)frameOffset, (int)frameLength).ToArray();
            await Assert.That(frame.Length >= SiteMetadataTokens.IcoPngHeaderLength).IsTrue();
            await Assert.That(frame.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature)).IsTrue();
            await Assert.That(BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(SiteMetadataTokens.IcoPngWidthOffset, 4)))
                .IsEqualTo(dimensions[index]);
            await Assert.That(BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(SiteMetadataTokens.IcoPngHeightOffset, 4)))
                .IsEqualTo(dimensions[index]);
        }
    }

    internal static async Task AssertBytesEqual(byte[] expected, byte[] actual)
        => await Assert.That(actual.AsSpan().SequenceEqual(expected)).IsTrue();
}
