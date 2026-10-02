using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-009: serialization contract attributes report machine-key literals.</summary>
internal sealed class LiteralMachineKeyAttributeContractTests
{
    private const string DiagnosticId = LiteralMachineKeyAnalyzer.DiagnosticId;

    [Test]
    public async Task QualifiedSerializationAttributeKeysAreDiagnosedAsync()
    {
        const string dataMemberKey = "\"member-name\"";
        const string jsonName = "\"json-name\"";
        const string qualifiedJsonName = "\"qualified-json-name\"";
        const string discriminator = "\"event-kind\"";
        const string source = """
            using System.Runtime.Serialization;
            using System.Text.Json.Serialization;
            [DataContract]
            internal sealed class Contract
            {
                [DataMember(Name = "member-name")]
                public string Member { get; init; } = string.Empty;
                [JsonPropertyName("json-name")]
                public string JsonMember { get; init; } = string.Empty;
                [global::System.Text.Json.Serialization.JsonPropertyName("qualified-json-name")]
                public string QualifiedMember { get; init; } = string.Empty;
            }
            [JsonPolymorphic(TypeDiscriminatorPropertyName = "event-kind")]
            internal abstract class EventContract { }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await AssertFindingsAtTokensAsync(findings, source, dataMemberKey, jsonName, qualifiedJsonName, discriminator);
    }

    [Test]
    public async Task HumanDescriptionAndConstantAttributeMetadataAreAllowedAsync()
    {
        const string source = """
            using System.ComponentModel;
            using System.Runtime.Serialization;
            internal static class Metadata
            {
                internal const string MemberName = "constant-member-name";
            }
            [Description("human-description")]
            [DataContract]
            internal sealed class Contract
            {
                [DataMember(Name = Metadata.MemberName)]
                public string Member { get; init; } = string.Empty;
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new LiteralMachineKeyAnalyzer(), source);
        await Assert.That(findings).IsEmpty();
    }

    private static async Task AssertFindingsAtTokensAsync(
        ImmutableArray<Diagnostic> findings,
        string source,
        params string[] expectedTokens)
    {
        await Assert.That(findings.Length).IsEqualTo(expectedTokens.Length);
        foreach (var token in expectedTokens)
        {
            var start = source.IndexOf(token, StringComparison.Ordinal);
            await Assert.That(start >= 0).IsTrue();
            var finding = findings.Single(item => item.Location.SourceSpan.Start == start);
            await Assert.That(finding.Id).IsEqualTo(DiagnosticId);
            await Assert.That(finding.Severity).IsEqualTo(DiagnosticSeverity.Error);
            await Assert.That(finding.Location.IsInSource).IsTrue();
            await Assert.That(finding.Location.SourceTree?.FilePath).IsEqualTo(AnalyzerFixture.ContractPath);
            await Assert.That(finding.Location.SourceSpan.Length).IsEqualTo(token.Length);
        }
    }
}
