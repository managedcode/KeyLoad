using System.Security.Cryptography;
using System.Text.Json;

namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>Validates the original context template against its distinct selected source bytes.</summary>
internal static class NativeCoverageRf3TemplateIdentity
{
    internal const string SourceRelativePath = "scripts/Features/CodeQuality/functional-coverage.server-image.dockerfile";
    private const string SourceTemplatesProperty = "sourceTemplates";
    private const string DockerfileSha256Property = "dockerfileSha256";
    private const string InvalidTemplate = "The original RF3 coverage template identity is invalid.";

    internal static string ReadAndRequire(byte[] originalContextBytes, ReadOnlySpan<byte> selectedSourceBytes)
    {
        using var context = JsonDocument.Parse(originalContextBytes);
        var declared = context.RootElement.GetProperty(SourceTemplatesProperty)
            .GetProperty(DockerfileSha256Property).GetString();
        return Require(declared, selectedSourceBytes);
    }

    private static string Require(string? declaredSha256, ReadOnlySpan<byte> selectedSourceBytes)
    {
        if (selectedSourceBytes.IsEmpty)
        { throw new InvalidOperationException(InvalidTemplate); }
        var expected = Convert.ToHexStringLower(SHA256.HashData(selectedSourceBytes));
        if (!string.Equals(declaredSha256, expected, StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidTemplate); }
        return expected;
    }
}
