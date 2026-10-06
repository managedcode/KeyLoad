using System.Globalization;
using System.Text.Json;
using System.Xml;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageLineUnionAssertions
{
    private const string RootName = "coverage";
    private const string PackageName = "name";
    private const string FileName = "filename";
    private const string ClassName = "name";
    private const string XmlLineElementName = "line";
    private const string NumberName = "number";
    private const string HitsName = "hits";
    private const string LinesValidAttribute = "lines-valid";
    private const string LinesCoveredAttribute = "lines-covered";
    private const string LineCountsName = "lineCounts";
    private const string LineValidProperty = "valid";
    private const string LineCoveredProperty = "covered";
    private const string BranchUnionName = "branchUnion";
    private const string InputCountProperty = "inputCount";
    private const string FileCountsName = "fileCounts";
    private const string UncoveredLocationsName = "uncoveredLocations";
    private const string ModuleName = "module";
    private const string SourceName = "source";
    private const string JsonLinesValidProperty = "linesValid";
    private const string JsonLinesCoveredProperty = "linesCovered";
    private const string JsonLineProperty = "line";
    private const string BranchUnionValue = "unmeasured; Cobertura branch pairs do not identify individual native branch outcomes";
    private const string InvalidReport = "The native Cobertura line-union output is invalid.";

    internal static async Task AssertMatchesNativeToolingProofAsync(string reportPath, long maximumBytes,
        JsonElement proof)
    {
        NativeCoverageOriginalInputUnionAssertions.AssertMatches(reportPath,
            proof.GetProperty(InputCountProperty).GetInt32(), maximumBytes);
        var root = LoadRoot(reportPath, maximumBytes);
        var summary = ReadUniqueLines(root);
        await Assert.That(ReadCount(root, LinesValidAttribute)).IsEqualTo(summary.RawCount);
        await Assert.That(ReadCount(root, LinesCoveredAttribute)).IsEqualTo(summary.RawCovered);
        var counts = proof.GetProperty(LineCountsName);
        await Assert.That(counts.GetProperty(LineValidProperty).GetInt64()).IsEqualTo(summary.UniqueCount);
        await Assert.That(counts.GetProperty(LineCoveredProperty).GetInt64()).IsEqualTo(summary.UniqueCovered);
        await Assert.That(proof.GetProperty(BranchUnionName).GetString()).IsEqualTo(BranchUnionValue);
        await NativeCoberturaBranchPairAssertions.AssertMatchesAsync(root, proof);
        AssertFiles(proof.GetProperty(FileCountsName), summary.Files);
        AssertUncovered(proof.GetProperty(UncoveredLocationsName), summary.Uncovered);
    }

    private static XmlElement LoadRoot(string path, long maximumBytes)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length <= 0 || info.Length > maximumBytes || info.LinkTarget is not null)
        { throw new InvalidDataException(InvalidReport); }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes
        };
        using var reader = XmlReader.Create(path, settings);
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        return document.DocumentElement is { LocalName: RootName } root ? root : throw new InvalidDataException(InvalidReport);
    }

    private static LineSummary ReadUniqueLines(XmlElement root)
    {
        var all = new HashSet<LineIdentity>();
        var covered = new HashSet<LineIdentity>();
        foreach (XmlNode package in root.SelectNodes("./packages/package") ?? throw new InvalidDataException(InvalidReport))
        { ReadPackage(package, all, covered); }
        if (all.Count == 0)
        { throw new InvalidDataException(InvalidReport); }
        return new LineSummary(all.Count, covered.Count, CountClassLines(root), CountCoveredClassLines(root),
            BuildFileCounts(all, covered), all.Where(identity => !covered.Contains(identity)).ToHashSet());
    }

    private static Dictionary<FileIdentity, FileLineCounts> BuildFileCounts(HashSet<LineIdentity> all,
        HashSet<LineIdentity> covered)
    {
        var files = new Dictionary<FileIdentity, FileLineCounts>();
        foreach (var line in all)
        {
            var identity = new FileIdentity(line.Package, line.File);
            if (!files.TryGetValue(identity, out var counts))
            { counts = new FileLineCounts(); files.Add(identity, counts); }
            counts.Valid++;
            if (covered.Contains(line))
            { counts.Covered++; }
        }
        return files;
    }

    private static void AssertFiles(JsonElement actual, Dictionary<FileIdentity, FileLineCounts> expected)
    {
        var seen = new HashSet<FileIdentity>();
        long valid = 0;
        long covered = 0;
        foreach (var file in actual.EnumerateArray())
        {
            var identity = new FileIdentity(file.GetProperty(ModuleName).GetString()!, file.GetProperty(SourceName).GetString()!);
            var counts = file.GetProperty(JsonLinesValidProperty).GetInt64();
            var hits = file.GetProperty(JsonLinesCoveredProperty).GetInt64();
            if (!seen.Add(identity) || !expected.TryGetValue(identity, out var expectedCounts) ||
                counts != expectedCounts.Valid || hits != expectedCounts.Covered)
            { throw new InvalidDataException(InvalidReport); }
            valid += counts;
            covered += hits;
        }
        if (seen.Count != expected.Count || valid != expected.Values.Sum(value => value.Valid) ||
            covered != expected.Values.Sum(value => value.Covered))
        { throw new InvalidDataException(InvalidReport); }
    }

    private static void AssertUncovered(JsonElement actual, HashSet<LineIdentity> expected)
    {
        var seen = new HashSet<LineIdentity>();
        foreach (var location in actual.EnumerateArray())
        {
            var identity = new LineIdentity(location.GetProperty(ModuleName).GetString()!,
                location.GetProperty(SourceName).GetString()!, location.GetProperty(JsonLineProperty).GetInt32());
            if (!seen.Add(identity) || !expected.Contains(identity))
            { throw new InvalidDataException(InvalidReport); }
        }
        if (!seen.SetEquals(expected))
        { throw new InvalidDataException(InvalidReport); }
    }

    private static void ReadPackage(XmlNode package, HashSet<LineIdentity> all, HashSet<LineIdentity> covered)
    {
        var packageName = package.Attributes?[PackageName]?.Value;
        if (string.IsNullOrWhiteSpace(packageName))
        { throw new InvalidDataException(InvalidReport); }
        foreach (XmlNode type in package.SelectNodes("./classes/class") ?? throw new InvalidDataException(InvalidReport))
        { ReadClass(packageName, type, all, covered); }
    }

    private static void ReadClass(string packageName, XmlNode type, HashSet<LineIdentity> all,
        HashSet<LineIdentity> covered)
    {
        var fileName = type.Attributes?[FileName]?.Value;
        if (string.IsNullOrWhiteSpace(type.Attributes?[ClassName]?.Value) || string.IsNullOrWhiteSpace(fileName))
        { throw new InvalidDataException(InvalidReport); }
        foreach (XmlNode line in type.SelectNodes("./lines/" + XmlLineElementName) ?? throw new InvalidDataException(InvalidReport))
        {
            var number = ParsePositive(line.Attributes?[NumberName]?.Value);
            var hits = ParseCount(line.Attributes?[HitsName]?.Value);
            var identity = new LineIdentity(packageName, fileName, number);
            all.Add(identity);
            if (hits > 0)
            { covered.Add(identity); }
        }
    }

    private static long CountClassLines(XmlElement root) => root.SelectNodes("./packages/package/classes/class/lines/" + XmlLineElementName)?.Count
        ?? throw new InvalidDataException(InvalidReport);

    private static long CountCoveredClassLines(XmlElement root)
    {
        long covered = 0;
        foreach (XmlNode line in root.SelectNodes("./packages/package/classes/class/lines/" + XmlLineElementName)
            ?? throw new InvalidDataException(InvalidReport))
        { if (ParseCount(line.Attributes?[HitsName]?.Value) > 0) { covered++; } }
        return covered;
    }

    private static long ReadCount(XmlElement root, string name) => ParseCount(root.GetAttribute(name));

    private static int ParsePositive(string? value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result <= 0)
        { throw new InvalidDataException(InvalidReport); }
        return result;
    }

    private static long ParseCount(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
        { throw new InvalidDataException(InvalidReport); }
        return result;
    }

    private readonly record struct LineIdentity(string Package, string File, int Number);
    private readonly record struct FileIdentity(string Package, string File);
    private sealed class FileLineCounts { internal long Valid { get; set; } internal long Covered { get; set; } }
    private sealed record LineSummary(long UniqueCount, long UniqueCovered, long RawCount, long RawCovered,
        Dictionary<FileIdentity, FileLineCounts> Files, HashSet<LineIdentity> Uncovered);
}
