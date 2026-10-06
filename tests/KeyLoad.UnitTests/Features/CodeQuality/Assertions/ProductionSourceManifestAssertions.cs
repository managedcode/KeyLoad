using System.Text.Json;
using System.Text.RegularExpressions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class ProductionSourceManifestAssertions
{
    private const int SchemaVersion = 3;
    private const int CentralInputCount = 6;
    private const int AnalyzerModuleIndex = 1;
    private const int AppHostModuleIndex = 2;
    private const int FirstProductIndex = 0;
    private const int MinimumOwnedSourceCount = 1;
    private const string Sha256Pattern = "\\A[0-9a-f]{64}\\z";
    private const string SchemaVersionProperty = "schemaVersion";
    private const string ProductsProperty = "compiledProducts";
    private const string ModuleProperty = "module";
    private const string RoleProperty = "role";
    private const string SourcesProperty = "sources";
    private const string PathProperty = "path";
    private const string Sha256Property = "sha256";
    private const string CompiledIdentityProperty = "compiledIdentity";
    private const string BindingCompleteProperty = "compiledSourceBindingComplete";
    private const string CompileReceiptProperty = "compileReceipt";
    private const string CentralInputCountProperty = "centralInputCount";
    private const string CompiledTestsManifestProperty = "compiledTestsManifest";
    private const string ContributorsProperty = "contributors";
    private const string RepositoryProperty = "repository";
    private const string ContractHashProperty = "contractSha256";
    private const string ProducerProperty = "compilationProducer";
    private const string SettingsHashProperty = "settingsSha256";
    private const string ScriptsProperty = "scripts";
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
    private const string ProducerBindingProperty = "producer";
    private const string VersionProperty = "version";
    private const string SourceCountProperty = "sourceCount";
    private const string SourceSetHashProperty = "sourceSetSha256";
    private const string CentralInputsProperty = "centralInputs";
    private const string BindingProperty = "binding";
    private const string SourceRevisionProperty = "sourceRevision";
    private const string ExpectedRepository = "managedcode/KeyLoad";
    private const string ExpectedProducerPath = "tests/KeyLoad.UnitTests/Features/CodeQuality/Build/FunctionalCompilationIdentity.targets";
    private const string ProducerPathProperty = "path";

    private static readonly string[] ExpectedModules =
    [
        "KeyLoad.Abstractions", "KeyLoad.Analyzers", "KeyLoad.AppHost", "KeyLoad.Artifacts",
        "KeyLoad.Cli", "KeyLoad.Client", "KeyLoad.Core", "KeyLoad.Diagnostics", "KeyLoad.Orleans",
        "KeyLoad.Query", "KeyLoad.Replication", "KeyLoad.Security", "KeyLoad.Server",
        "KeyLoad.ServiceDefaults", "KeyLoad.Storage.IO", "KeyLoad.Storage.ZoneTree"
    ];

    internal static async Task AssertManifestAsync(string evidenceRoot)
    {
        var manifestPath = Path.Combine(evidenceRoot, "functional-coverage.production-source-manifest.json");
        using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath));
        var manifest = document.RootElement;
        await AssertFieldsAsync(manifest,
        [SchemaVersionProperty, "sourceRevision", RepositoryProperty, ContractHashProperty, ProductsProperty,
            CompiledTestsManifestProperty, ProducerProperty, ContributorsProperty, SettingsHashProperty, ScriptsProperty]);
        await Assert.That(manifest.GetProperty(SchemaVersionProperty).GetInt32()).IsEqualTo(SchemaVersion);
        await Assert.That(Regex.IsMatch(manifest.GetProperty(SourceRevisionProperty).GetString()!, "\\A[0-9a-f]{40}\\z", RegexOptions.CultureInvariant)).IsTrue();
        await Assert.That(manifest.GetProperty(RepositoryProperty).GetString()).IsEqualTo(ExpectedRepository);
        await AssertHashAsync(manifest.GetProperty(ContractHashProperty).GetString());
        await AssertHashAsync(manifest.GetProperty(SettingsHashProperty).GetString());
        await ProductionSourceManifestImageAssertions.AssertSettingsAsync(evidenceRoot, manifest.GetProperty(SettingsHashProperty).GetString()!);
        var producer = manifest.GetProperty(ProducerProperty);
        await AssertFieldsAsync(producer, [ProducerPathProperty, Sha256Property]);
        await Assert.That(producer.GetProperty(ProducerPathProperty).GetString()).IsEqualTo(ExpectedProducerPath);
        await AssertHashAsync(producer.GetProperty(Sha256Property).GetString());
        await AssertProductsAsync(manifest.GetProperty(ProductsProperty));
        await ProductionSourceManifestImageAssertions.AssertImagesAsync(evidenceRoot, manifest.GetProperty(CompiledTestsManifestProperty),
            manifest.GetProperty(SettingsHashProperty).GetString()!);
        await ProductionSourceManifestContributorAssertions.AssertAsync(manifest.GetProperty(ContributorsProperty),
            ProductionSourceManifestProcess.RepositoryRoot);
        await ProductionSourceManifestScriptAssertions.AssertAsync(manifest.GetProperty(ScriptsProperty));
    }

    private static async Task AssertProductsAsync(JsonElement productRows)
    {
        var products = productRows.EnumerateArray().ToArray();
        await Assert.That(products.Length).IsEqualTo(ExpectedModules.Length);
        for (var index = FirstProductIndex; index < products.Length; index++)
        {
            var product = products[index];
            await AssertFieldsAsync(product, [ModuleProperty, RoleProperty, SourcesProperty, CompiledIdentityProperty]);
            await Assert.That(product.GetProperty(ModuleProperty).GetString()).IsEqualTo(ExpectedModules[index]);
            var expectedRole = index is AnalyzerModuleIndex or AppHostModuleIndex ? "infrastructure" : "production";
            await Assert.That(product.GetProperty(RoleProperty).GetString()).IsEqualTo(expectedRole);
            var sourceRows = product.GetProperty(SourcesProperty).EnumerateArray().ToArray();
            await Assert.That(sourceRows.Length).IsGreaterThan(MinimumOwnedSourceCount);
            foreach (var source in sourceRows)
            {
                var path = source.GetProperty(PathProperty).GetString()!;
                await AssertFieldsAsync(source, [PathProperty, Sha256Property]);
                await Assert.That(path.StartsWith("src/" + ExpectedModules[index] + "/", StringComparison.Ordinal)).IsTrue();
                await Assert.That(Regex.IsMatch(source.GetProperty(Sha256Property).GetString()!, Sha256Pattern, RegexOptions.CultureInvariant)).IsTrue();
            }
            var compiledIdentity = product.GetProperty(CompiledIdentityProperty);
            await AssertFieldsAsync(compiledIdentity,
            [DllProperty, PdbProperty, DllHashProperty, PdbHashProperty, ModuleNameProperty, IdentityToolHashProperty,
                OriginalCompilationRootProperty, InspectedSourceRootProperty, MvidProperty, PdbGuidProperty, PdbStampProperty,
                DocumentsProperty, MissingSourcesProperty, BindingCompleteProperty, CompileReceiptProperty, QualificationProperty]);
            await AssertFieldsAsync(compiledIdentity.GetProperty(CompileReceiptProperty),
            [VersionProperty, SourceCountProperty, SourceSetHashProperty, CentralInputCountProperty,
                CentralInputsProperty, ProducerBindingProperty, BindingProperty]);
            await Assert.That(compiledIdentity.GetProperty(BindingCompleteProperty).GetBoolean()).IsTrue();
            await Assert.That(compiledIdentity.GetProperty(CompileReceiptProperty).GetProperty(CentralInputCountProperty).GetInt32()).IsEqualTo(CentralInputCount);
            await AssertHashAsync(compiledIdentity.GetProperty(DllHashProperty).GetString());
            await AssertHashAsync(compiledIdentity.GetProperty(PdbHashProperty).GetString());
            await ProductionSourceManifestNativeIdentityAssertions.AssertAsync(compiledIdentity, ExpectedModules[index]);
        }
    }

    private static async Task AssertHashAsync(string? hash)
        => await Assert.That(Regex.IsMatch(hash ?? string.Empty, Sha256Pattern, RegexOptions.CultureInvariant)).IsTrue();

    private static async Task AssertFieldsAsync(JsonElement element, string[] expected)
    {
        var actual = element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        await Assert.That(actual).IsEquivalentTo(expected.Order(StringComparer.Ordinal), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

}
