using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class ProductionSourceManifestNativeIdentityAssertions
{
    private const int Zero = 0;
    private const int GuidBytes = 16;
    private const int NativeDebugIdentifierBytes = 20;
    private const int SingleCodeViewRecord = 1;
    private const string GuidFormat = "D";
    private const string DllExtension = ".dll";
    private const string DllProperty = "dll";
    private const string PdbProperty = "pdb";
    private const string ModuleNameProperty = "moduleName";
    private const string PdbGuidProperty = "pdbGuid";
    private const string PdbStampProperty = "pdbStamp";
    private const string InvalidSource = "The native identity oracle input is outside its original compilation root.";

    internal static async Task AssertAsync(JsonElement identity, string expectedModule)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var dll = ResolveOriginal(identity, DllProperty);
        var pdb = ResolveOriginal(identity, PdbProperty);
        await AssertDllAsync(dll, identity, expectedModule, token);
        await AssertPdbAsync(pdb, identity, token);
    }

    private static async Task AssertDllAsync(string path, JsonElement identity, string expectedModule,
        CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        await Assert.That(Convert.ToHexStringLower(hash))
            .IsEqualTo(identity.GetProperty(NativeCoverageImageFields.DllSha256).GetString());
        stream.Position = Zero;
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var reader = pe.GetMetadataReader();
        var module = reader.GetModuleDefinition();
        var name = reader.GetString(module.Name);
        await Assert.That(name).IsEqualTo(expectedModule + DllExtension);
        await Assert.That(name).IsEqualTo(identity.GetProperty(ModuleNameProperty).GetString());
        await Assert.That(reader.GetGuid(module.Mvid).ToString(GuidFormat))
            .IsEqualTo(identity.GetProperty(NativeCoverageImageFields.Mvid).GetString());
        var entries = pe.ReadDebugDirectory().Where(entry => entry.Type == DebugDirectoryEntryType.CodeView).ToArray();
        await Assert.That(entries.Length).IsEqualTo(SingleCodeViewRecord);
        var codeView = pe.ReadCodeViewDebugDirectoryData(entries[Zero]);
        await Assert.That(codeView.Guid.ToString(GuidFormat)).IsEqualTo(identity.GetProperty(PdbGuidProperty).GetString());
        await Assert.That(entries[Zero].Stamp).IsEqualTo(identity.GetProperty(PdbStampProperty).GetUInt32());
    }

    private static async Task AssertPdbAsync(string path, JsonElement identity, CancellationToken cancellationToken)
    {
        using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        await Assert.That(Convert.ToHexStringLower(hash))
            .IsEqualTo(identity.GetProperty(NativeCoverageImageFields.PdbSha256).GetString());
        stream.Position = Zero;
        using var provider = MetadataReaderProvider.FromPortablePdbStream(stream, MetadataStreamOptions.LeaveOpen);
        var identifier = provider.GetMetadataReader().DebugMetadataHeader!.Id;
        await Assert.That(identifier.Length).IsEqualTo(NativeDebugIdentifierBytes);
        await Assert.That(new Guid(identifier.AsSpan().Slice(Zero, GuidBytes)).ToString(GuidFormat))
            .IsEqualTo(identity.GetProperty(PdbGuidProperty).GetString());
        await Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(identifier.AsSpan().Slice(GuidBytes)))
            .IsEqualTo(identity.GetProperty(PdbStampProperty).GetUInt32());
    }

    private static string ResolveOriginal(JsonElement identity, string property)
    {
        var root = Path.GetFullPath(ProductionSourceManifestProcess.RepositoryRoot) + Path.DirectorySeparatorChar;
        var relative = identity.GetProperty(property).GetString()!;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        return !Path.IsPathRooted(relative) && path.StartsWith(root, StringComparison.Ordinal)
            ? path : throw new InvalidOperationException(InvalidSource);
    }
}
