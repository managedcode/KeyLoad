using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class ProductionSourceManifestImageAssertions
{
    private const int ExpectedReferenceCount = 4;
    private const int CentralInputCount = 6;
    private const int UnitIndex = 0;
    private const int ScalarIndex = 1;
    private const int RecoveryIndex = 2;
    private const int Rf3Index = 3;
    private const string UnitSuite = "unit";
    private const string ScalarSuite = "unit-scalar";
    private const string RecoverySuite = "recovery";
    private const string Rf3Suite = "rf3";
    private const string UnitFileName = "functional-coverage.test-image.unit.json";
    private const string RecoveryFileName = "functional-coverage.test-image.recovery.json";
    private const string Rf3FileName = "functional-coverage.test-image.rf3.json";
    private const string SettingsFileName = "functional-coverage.production.settings.xml";
    private const string SettingsSourcePath = "scripts/Features/CodeQuality/functional-coverage.production.settings.xml";
    private const string SchemaVersionProperty = "schemaVersion";
    private const string SuiteProperty = "suite";
    private const string FileNameProperty = "fileName";
    private const string HashProperty = "sha256";
    private const string ProjectProperty = "project";
    private const string SourcesProperty = "sources";
    private const string BuildInputsProperty = "buildInputs";
    private const string IdentityProperty = "compiledIdentity";
    private const string QualificationProperty = "qualification";
    private const string DllProperty = "dll";
    private const string PdbProperty = "pdb";
    private const string DllHashProperty = "dllSha256";
    private const string PdbHashProperty = "pdbSha256";
    private const string ModuleNameProperty = "moduleName";
    private const string IdentityToolHashProperty = "compiledIdentityToolSha256";
    private const string OriginalCompilationRootProperty = "originalCompilationRoot";
    private const string InspectedSourceRootProperty = "inspectedSourceRoot";
    private const string MvidProperty = "mvid";
    private const string PdbGuidProperty = "pdbGuid";
    private const string PdbStampProperty = "pdbStamp";
    private const string DocumentsProperty = "documents";
    private const string MissingSourcesProperty = "sourceFilesWithoutPdbDocuments";
    private const string BindingCompleteProperty = "compiledSourceBindingComplete";
    private const string CompileReceiptProperty = "compileReceipt";
    private const string VersionProperty = "version";
    private const string SourceCountProperty = "sourceCount";
    private const string SourceSetHashProperty = "sourceSetSha256";
    private const string CentralInputCountProperty = "centralInputCount";
    private const string CentralInputsProperty = "centralInputs";
    private const string ProducerProperty = "producer";
    private const string BindingProperty = "binding";

    internal static async Task AssertImagesAsync(string evidenceRoot, JsonElement references, string settingsHash)
    {
        await Assert.That(references.GetArrayLength()).IsEqualTo(ExpectedReferenceCount);
        var unitHash = references[UnitIndex].GetProperty(HashProperty).GetString();
        await AssertReferenceAsync(references[UnitIndex], UnitSuite, UnitFileName);
        await AssertReferenceAsync(references[ScalarIndex], ScalarSuite, UnitFileName);
        await Assert.That(references[ScalarIndex].GetProperty(HashProperty).GetString()).IsEqualTo(unitHash);
        await AssertReferenceAsync(references[RecoveryIndex], RecoverySuite, RecoveryFileName);
        await AssertReferenceAsync(references[Rf3Index], Rf3Suite, Rf3FileName);
        await Assert.That(references[RecoveryIndex].GetProperty(FileNameProperty).GetString()).IsNotEqualTo(
            references[Rf3Index].GetProperty(FileNameProperty).GetString());
        await AssertImageAsync(evidenceRoot, references[UnitIndex], "tests/KeyLoad.UnitTests/KeyLoad.UnitTests.csproj");
        await AssertImageAsync(evidenceRoot, references[RecoveryIndex], "tests/KeyLoad.RecoveryTests/KeyLoad.RecoveryTests.csproj");
        await AssertImageAsync(evidenceRoot, references[Rf3Index], "tests/KeyLoad.IntegrationTests/KeyLoad.IntegrationTests.csproj");
        await AssertSettingsAsync(evidenceRoot, settingsHash);
    }

    internal static async Task AssertSettingsAsync(string evidenceRoot, string expectedHash)
    {
        var source = Path.Combine(ProductionSourceManifestProcess.RepositoryRoot, SettingsSourcePath);
        var captured = Path.Combine(evidenceRoot, SettingsFileName);
        var sourceBytes = await File.ReadAllBytesAsync(source);
        var capturedBytes = await File.ReadAllBytesAsync(captured);
        await Assert.That(capturedBytes).IsEquivalentTo(sourceBytes, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(capturedBytes))).IsEqualTo(expectedHash);
    }

    private static async Task AssertReferenceAsync(JsonElement reference, string suite, string fileName)
    {
        await AssertFieldsAsync(reference, [SuiteProperty, FileNameProperty, HashProperty]);
        await Assert.That(reference.GetProperty(SuiteProperty).GetString()).IsEqualTo(suite);
        await Assert.That(reference.GetProperty(FileNameProperty).GetString()).IsEqualTo(fileName);
        await AssertHashAsync(reference.GetProperty(HashProperty).GetString());
    }

    private static async Task AssertImageAsync(string evidenceRoot, JsonElement reference, string expectedProject)
    {
        var fileName = reference.GetProperty(FileNameProperty).GetString()!;
        await Assert.That(Path.GetFileName(fileName) == fileName
            && !fileName.Contains('/', StringComparison.Ordinal)
            && !fileName.Contains('\\', StringComparison.Ordinal)).IsTrue();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(evidenceRoot, fileName));
        await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqualTo(reference.GetProperty(HashProperty).GetString());
        using var document = JsonDocument.Parse(bytes);
        var image = document.RootElement;
        await AssertFieldsAsync(image,
            [SchemaVersionProperty, ProjectProperty, SourcesProperty, BuildInputsProperty, IdentityProperty, QualificationProperty]);
        await Assert.That(image.GetProperty(ProjectProperty).GetString()).IsEqualTo(expectedProject);
        var compiled = image.GetProperty(IdentityProperty);
        await AssertFieldsAsync(compiled,
            [DllProperty, PdbProperty, DllHashProperty, PdbHashProperty, ModuleNameProperty, IdentityToolHashProperty,
                OriginalCompilationRootProperty, InspectedSourceRootProperty, MvidProperty, PdbGuidProperty, PdbStampProperty,
                DocumentsProperty, MissingSourcesProperty, BindingCompleteProperty, CompileReceiptProperty, QualificationProperty]);
        await Assert.That(compiled.GetProperty(BindingCompleteProperty).GetBoolean()).IsTrue();
        var receipt = compiled.GetProperty(CompileReceiptProperty);
        await AssertFieldsAsync(receipt,
            [VersionProperty, SourceCountProperty, SourceSetHashProperty, CentralInputCountProperty,
                CentralInputsProperty, ProducerProperty, BindingProperty]);
        await Assert.That(receipt.GetProperty(CentralInputCountProperty).GetInt32()).IsEqualTo(CentralInputCount);
        await AssertHashAsync(compiled.GetProperty(DllHashProperty).GetString());
        await AssertHashAsync(compiled.GetProperty(PdbHashProperty).GetString());
        await ProductionSourceManifestNativeIdentityAssertions.AssertAsync(compiled,
            Path.GetFileNameWithoutExtension(expectedProject));
    }

    private static async Task AssertFieldsAsync(JsonElement element, string[] expected)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        await Assert.That(actual).IsEquivalentTo(expected.Order(StringComparer.Ordinal),
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static async Task AssertHashAsync(string? hash)
    {
        await Assert.That(Regex.IsMatch(hash ?? string.Empty, "\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)).IsTrue();
    }
}
