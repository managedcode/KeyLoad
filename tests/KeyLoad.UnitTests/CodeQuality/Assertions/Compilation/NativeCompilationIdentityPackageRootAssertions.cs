using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Security.Cryptography;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class NativeCompilationIdentityPackageRootAssertions
{
    private const int SuccessfulExit = 0;
    private const int RejectedExit = 1;
    private const int SingleCodeViewRecord = 1;
    private const int FirstCodeViewRecord = 0;
    private const int GuidByteOffset = 0;
    private const int GuidByteCount = 16;
    private const int PdbStampByteOffset = 16;
    private const int PdbStampByteCount = 4;
    private const int PdbIdentifierBytes = GuidByteCount + PdbStampByteCount;
    private const string GuidFormat = "N";
    private const string GuardMessage = "A non-generated project compile source is outside the owned project tree or uses an unsupported path.";
    private const string MetadataPrefix = "KeyLoad.FunctionalCompileIdentity.";
    private const string VersionKey = MetadataPrefix + "Version";
    private const string SourceCountKey = MetadataPrefix + "SourceCount";
    private const string CentralCountKey = MetadataPrefix + "CentralCount";
    private const string ProducerKey = MetadataPrefix + "Producer";
    private const string SourceKey = MetadataPrefix + "Source";
    private const string CentralKey = MetadataPrefix + "Central";

    internal static async Task SuccessfulProcessAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(SuccessfulExit);
        await Assert.That(result.OriginalExitJoined && result.StandardOutputJoined && result.StandardErrorJoined && result.ProcessDisposed).IsTrue();
    }

    internal static async Task CounterfeitRejectedAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(RejectedExit);
        await Assert.That(result.StandardOutput + result.StandardError).Contains(GuardMessage);
        await Assert.That(result.OriginalExitJoined && result.StandardOutputJoined && result.StandardErrorJoined && result.ProcessDisposed).IsTrue();
    }

    internal static async Task<string[]> ReadMetadataAsync(NativeCompilationIdentityPackageRootFixture fixture,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString(GuidFormat), isCollectible: true);
        string[] metadata;
        try
        {
            using var image = new MemoryStream(bytes, writable: false);
            var assembly = context.LoadFromStream(image);
            metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(attribute => attribute.Key.StartsWith(MetadataPrefix, StringComparison.Ordinal))
                .Select(attribute => attribute.Key + "=" + attribute.Value)
                .Order(StringComparer.Ordinal).ToArray();
        }
        finally
        {
            context.Unload();
        }
        var expected = await ExpectedMetadataAsync(fixture, cancellationToken);
        await Assert.That(metadata).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        return metadata;
    }

    internal static async Task AssertNativeImageAsync(NativeCompilationIdentityPackageRootFixture fixture,
        CancellationToken cancellationToken)
    {
        using var dll = File.OpenRead(fixture.DllPath);
        var dllHash = await SHA256.HashDataAsync(dll, cancellationToken);
        dll.Position = 0;
        using var pe = new PEReader(dll, PEStreamOptions.LeaveOpen);
        var module = pe.GetMetadataReader().GetModuleDefinition();
        await Assert.That(pe.HasMetadata).IsTrue();
        await Assert.That(pe.GetMetadataReader().GetGuid(module.Mvid)).IsNotEqualTo(Guid.Empty);
        await Assert.That(dllHash.Length).IsEqualTo(SHA256.HashSizeInBytes);
        var entries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        await Assert.That(entries.Length).IsEqualTo(SingleCodeViewRecord);
        var codeView = pe.ReadCodeViewDebugDirectoryData(entries[FirstCodeViewRecord]);
        using var pdb = File.OpenRead(fixture.PdbPath);
        var pdbHash = await SHA256.HashDataAsync(pdb, cancellationToken);
        pdb.Position = 0;
        using var provider = MetadataReaderProvider.FromPortablePdbStream(pdb, MetadataStreamOptions.LeaveOpen);
        var identifier = provider.GetMetadataReader().DebugMetadataHeader!.Id;
        await Assert.That(identifier.Length).IsEqualTo(PdbIdentifierBytes);
        await Assert.That(new Guid(identifier.AsSpan(GuidByteOffset, GuidByteCount))).IsEqualTo(codeView.Guid);
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(identifier.AsSpan(PdbStampByteOffset, PdbStampByteCount))).IsEqualTo(entries[FirstCodeViewRecord].Stamp);
        await Assert.That(pdbHash.Length).IsEqualTo(SHA256.HashSizeInBytes);
    }

    internal static async Task<byte[][]> CaptureImagesAsync(NativeCompilationIdentityPackageRootFixture fixture,
        CancellationToken cancellationToken)
        => [await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken),
            await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken)];

    internal static async Task AssertImagesUnchangedAsync(NativeCompilationIdentityPackageRootFixture fixture,
        byte[][] original, CancellationToken cancellationToken)
    {
        await Assert.That((await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken)).AsSpan()
            .SequenceEqual(original[0])).IsTrue();
        await Assert.That((await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken)).AsSpan()
            .SequenceEqual(original[1])).IsTrue();
    }

    private static async Task<string[]> ExpectedMetadataAsync(NativeCompilationIdentityPackageRootFixture fixture,
        CancellationToken cancellationToken)
    {
        var values = new List<string>
        {
            VersionKey + "=1",
            SourceCountKey + "=1",
            CentralCountKey + "=6",
            ProducerKey + "=" + NativeCompilationIdentityPackageRootFixture.ProducerRelativePath + "|" +
                await HashAsync(Path.Combine(fixture.Root, NativeCompilationIdentityPackageRootFixture.ProducerRelativePath), cancellationToken),
            SourceKey + "=" + NativeCompilationIdentityPackageRootFixture.SourceRelativePath + "|" +
                await HashAsync(Path.Combine(fixture.Root, NativeCompilationIdentityPackageRootFixture.SourceRelativePath), cancellationToken)
        };
        foreach (var relative in NativeCompilationIdentityPackageRootFixture.CentralRelativePaths)
        {
            var canonical = relative.Replace(Path.DirectorySeparatorChar, '/');
            values.Add(CentralKey + "=" + canonical + "|" + await HashAsync(Path.Combine(fixture.Root, relative), cancellationToken));
        }
        return [.. values.Order(StringComparer.Ordinal)];
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }
}
