using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.UnitTests.Features.CodeQuality.Helpers;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class NativeCompiledDocumentBudgetAssertions
{
    private const int NativeAndGeneratedDocuments = 2;
    private const string GeneratedPath = "obj/native-generated.cs";
    private const string SourceRefusedProperty = "sourceRefused";
    private const string GeneratedRefusedProperty = "generatedRefused";
    private const string FirstProperty = "first";
    private const string HealthyProperty = "healthy";
    private const string CompiledSourceBindingCompleteProperty = "compiledSourceBindingComplete";
    private const string SourceFilesWithoutPdbDocumentsProperty = "sourceFilesWithoutPdbDocuments";
    private const string DocumentsProperty = "documents";
    private const string PathProperty = "path";
    private const string GeneratedProperty = "generated";
    private const string Sha256Property = "sha256";

    internal static async Task VerifyAsync(ProductionSourceManifestProcessResult result)
    {
        await NativePathMapAssertions.SuccessfulProcessAsync(result);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var root = json.RootElement;
        await Assert.That(root.GetProperty(SourceRefusedProperty).GetBoolean()).IsTrue();
        await Assert.That(root.GetProperty(GeneratedRefusedProperty).GetBoolean()).IsTrue();
        var first = root.GetProperty(FirstProperty);
        var healthy = root.GetProperty(HealthyProperty);
        await Assert.That(healthy.GetRawText()).IsEqualTo(first.GetRawText());
        await Assert.That(first.GetProperty(CompiledSourceBindingCompleteProperty).GetBoolean()).IsTrue();
        await Assert.That(first.GetProperty(SourceFilesWithoutPdbDocumentsProperty).EnumerateArray()).IsEmpty();
        var documents = first.GetProperty(DocumentsProperty).EnumerateArray().ToArray();
        await Assert.That(documents.Length).IsEqualTo(NativeAndGeneratedDocuments);
        var owned = documents.Single(document => document.GetProperty(PathProperty).GetString() == NativePathMapFixture.SourceFileName);
        await Assert.That(owned.GetProperty(GeneratedProperty).GetBoolean()).IsFalse();
        await Assert.That(owned.GetProperty(Sha256Property).GetString())
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(NativePathMapFixture.OriginalSourceBytes)));
        var generated = documents.Single(document => document.GetProperty(PathProperty).GetString() == GeneratedPath);
        await Assert.That(generated.GetProperty(GeneratedProperty).GetBoolean()).IsTrue();
        await Assert.That(generated.GetProperty(Sha256Property).GetString())
            .IsEqualTo(Convert.ToHexStringLower(SHA256.HashData(NativeCompiledDocumentBudgetScenario.GeneratedBytes)));
    }
}
