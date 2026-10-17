using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class NativePathMapAssertions
{
    private const int SuccessfulExitCode = 0;
    private const int EmptyCollectionCount = 0;
    private const int SingleSourceDocument = 1;
    private const string Documents = "documents";
    private const string MissingDocuments = "sourceFilesWithoutPdbDocuments";
    private const string BindingComplete = "compiledSourceBindingComplete";
    private const string PathProperty = "path";
    private const string Sha256Property = "sha256";
    private const string Generated = "generated";
    private const string DllSha256 = "dllSha256";
    private const string PdbSha256 = "pdbSha256";
    private const string ExternalPrefix = "external/";
    private static readonly Guid Sha256Algorithm = new("8829d00f-11b8-4213-878b-770e8597ac16");

    internal static async Task SuccessfulProcessAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.ExitCode).IsEqualTo(SuccessfulExitCode).Because(result.StandardError);
        await Assert.That(result.StandardError).IsEmpty();
        await SettledAsync(result);
    }

    internal static async Task SettledAsync(ProductionSourceManifestProcessResult result)
    {
        await Assert.That(result.OriginalExitJoined).IsTrue();
        await Assert.That(result.StandardOutputJoined).IsTrue();
        await Assert.That(result.StandardErrorJoined).IsTrue();
        await Assert.That(result.ProcessDisposed).IsTrue();
    }

    internal static async Task RegularCompilerFilesAsync(NativePathMapFixture fixture)
    {
        foreach (var path in new[] { fixture.SourcePath, fixture.DllPath, fixture.PdbPath })
        {
            var file = new FileInfo(path);
            await Assert.That(file.Exists).IsTrue();
            await Assert.That(file.LinkTarget).IsNull();
            await Assert.That((file.Attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == default).IsTrue();
            await Assert.That(file.Length > EmptyCollectionCount).IsTrue();
        }
    }

    internal static async Task NativeDocumentAsync(NativePathMapFixture fixture, string mappedRoot)
    {
        using var stream = File.OpenRead(fixture.PdbPath);
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream);
        var reader = provider.GetMetadataReader();
        await Assert.That(reader.Documents.Count).IsEqualTo(SingleSourceDocument);
        var document = reader.GetDocument(reader.Documents.Single());
        await Assert.That(reader.GetString(document.Name).Replace('\\', '/'))
            .IsEqualTo(mappedRoot + NativePathMapFixture.SourceFileName);
        await Assert.That(reader.GetGuid(document.HashAlgorithm)).IsEqualTo(Sha256Algorithm);
        await Assert.That(reader.GetBlobBytes(document.Hash).AsSpan()
            .SequenceEqual(SHA256.HashData(NativePathMapFixture.OriginalSourceBytes))).IsTrue();
    }

    internal static async Task CanonicalBindingAsync(NativePathMapFixture fixture,
        ProductionSourceManifestProcessResult result, CancellationToken cancellationToken)
    {
        await SuccessfulProcessAsync(result);
        using var receipt = JsonDocument.Parse(result.StandardOutput);
        var identity = receipt.RootElement;
        await Assert.That(identity.GetProperty(BindingComplete).GetBoolean()).IsTrue();
        await Assert.That(identity.GetProperty(MissingDocuments).GetArrayLength()).IsEqualTo(EmptyCollectionCount);
        await Assert.That(identity.GetProperty(Documents).GetArrayLength()).IsEqualTo(SingleSourceDocument);
        var source = identity.GetProperty(Documents).EnumerateArray().Single();
        await Assert.That(source.GetProperty(PathProperty).GetString()).IsEqualTo(NativePathMapFixture.SourceFileName);
        await Assert.That(source.GetProperty(Generated).GetBoolean()).IsFalse();
        await Assert.That(source.GetProperty(Sha256Property).GetString())
            .IsEqualTo(Hash(NativePathMapFixture.OriginalSourceBytes));
        await Assert.That(identity.GetProperty(DllSha256).GetString())
            .IsEqualTo(Hash(await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken)));
        await Assert.That(identity.GetProperty(PdbSha256).GetString())
            .IsEqualTo(Hash(await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken)));
    }

    internal static async Task UnknownRootUnboundAsync(ProductionSourceManifestProcessResult result)
    {
        await SuccessfulProcessAsync(result);
        using var receipt = JsonDocument.Parse(result.StandardOutput);
        var identity = receipt.RootElement;
        await Assert.That(identity.GetProperty(BindingComplete).GetBoolean()).IsFalse();
        var missing = identity.GetProperty(MissingDocuments).EnumerateArray().Single();
        await Assert.That(missing.GetProperty(PathProperty).GetString()).IsEqualTo(NativePathMapFixture.SourceFileName);
        var document = identity.GetProperty(Documents).EnumerateArray().Single();
        var expected = ExternalPrefix + Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(
            NativePathMapFixture.UnknownMap + NativePathMapFixture.SourceFileName)));
        await Assert.That(document.GetProperty(PathProperty).GetString()).IsEqualTo(expected);
        await Assert.That(document.GetProperty(Generated).GetBoolean()).IsTrue();
        await Assert.That(document.GetProperty(Sha256Property).GetString())
            .IsEqualTo(Hash(NativePathMapFixture.OriginalSourceBytes));
        await Assert.That(result.StandardOutput.Contains(NativePathMapFixture.UnknownMap, StringComparison.Ordinal)).IsFalse();
        await Assert.That(result.StandardError.Contains(NativePathMapFixture.UnknownMap, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task ImagesUnchangedAsync(NativePathMapFixture fixture, byte[] originalDll,
        byte[] originalPdb, CancellationToken cancellationToken)
    {
        var dll = await File.ReadAllBytesAsync(fixture.DllPath, cancellationToken);
        var pdb = await File.ReadAllBytesAsync(fixture.PdbPath, cancellationToken);
        await Assert.That(dll.AsSpan().SequenceEqual(originalDll)).IsTrue();
        await Assert.That(pdb.AsSpan().SequenceEqual(originalPdb)).IsTrue();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
