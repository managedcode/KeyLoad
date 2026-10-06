using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Assertions;

internal static class ProductionSourceManifestScriptAssertions
{
    private const int MaximumInventoryEntries = 5000;
    private const long MaximumScriptBytes = 33554432;
    private const int FirstIndex = 0;
    private const string ScriptPrefix = "functional-coverage";
    private const string DockerIgnoreName = ".dockerignore";
    private const string ScriptDirectory = "scripts/Features/CodeQuality";
    private const string NameProperty = "name";
    private const string HashProperty = "sha256";
    private static readonly string[] AllowedExtensions = [".ps1", ".mjs", ".sh", ".xml", ".json", ".dockerfile"];

    internal static async Task AssertAsync(JsonElement scripts)
    {
        var directory = Path.Combine(ProductionSourceManifestProcess.RepositoryRoot, ScriptDirectory);
        var expected = GetExpectedNames(directory);
        await Assert.That(scripts.ValueKind).IsEqualTo(JsonValueKind.Array);
        await Assert.That(scripts.GetArrayLength()).IsEqualTo(expected.Length);
        for (var index = FirstIndex; index < expected.Length; index++)
        {
            var row = scripts[index];
            await AssertFieldsAsync(row);
            await Assert.That(row.GetProperty(NameProperty).GetString()).IsEqualTo(expected[index]);
            var path = Path.Combine(directory, expected[index]);
            var info = new FileInfo(path);
            await Assert.That(info.LinkTarget).IsNull();
            await Assert.That(info.Length).IsLessThanOrEqualTo(MaximumScriptBytes);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            await Assert.That(row.GetProperty(HashProperty).GetString()).IsEqualTo(hash);
        }
    }

    private static string[] GetExpectedNames(string directory)
    {
        var directoryInfo = new DirectoryInfo(directory);
        if (directoryInfo.LinkTarget is not null)
        { throw new InvalidOperationException(); }
        var names = new List<string>();
        var entries = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            if (entries >= MaximumInventoryEntries)
            { throw new InvalidOperationException(); }
            entries++;
            var item = new FileInfo(path);
            if (item.LinkTarget is not null || Directory.Exists(path))
            { throw new InvalidOperationException(); }
            if (item.Name.StartsWith(ScriptPrefix, StringComparison.Ordinal) && !IsIncludedScript(item.Name))
            {
                throw new InvalidOperationException();
            }
            if (item.Name.Equals(DockerIgnoreName, StringComparison.Ordinal) || IsIncludedScript(item.Name))
            {
                names.Add(item.Name);
            }
        }
        names.Sort(StringComparer.Ordinal);
        return names.ToArray();
    }

    private static bool IsIncludedScript(string name)
        => name.StartsWith(ScriptPrefix, StringComparison.Ordinal) &&
            AllowedExtensions.Any(extension => name.EndsWith(extension, StringComparison.Ordinal));

    private static async Task AssertFieldsAsync(JsonElement row)
    {
        var actual = row.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal);
        await Assert.That(actual).IsEquivalentTo(new[] { NameProperty, HashProperty },
            TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }
}
