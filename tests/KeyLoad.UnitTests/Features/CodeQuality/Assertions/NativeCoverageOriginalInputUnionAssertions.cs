using System.Globalization;
using System.Xml;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageOriginalInputUnionAssertions
{
    private const int ExpectedInputCount = 3;
    private const string MergedReportName = "merged.cobertura";
    private const string InputReportPrefix = "input-";
    private const string InputReportSuffix = ".cobertura";
    private const string CoverageRootName = "coverage";
    private const string PackagesElementName = "packages";
    private const string PackageElementName = "package";
    private const string ClassesElementName = "classes";
    private const string ClassElementName = "class";
    private const string LinesElementName = "lines";
    private const string LineElementName = "line";
    private const string PackageNameAttribute = "name";
    private const string FileNameAttribute = "filename";
    private const string ClassNameAttribute = "name";
    private const string NumberAttribute = "number";
    private const string HitsAttribute = "hits";
    private const string InvalidReport = "The native original Cobertura line union is invalid.";

    internal static void AssertMatches(string mergedPath, int inputCount, long maximumBytes)
    {
        if (inputCount != ExpectedInputCount || maximumBytes <= 0
            || Path.GetFileName(mergedPath) != MergedReportName)
        {
            throw Invalid();
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(mergedPath)) ?? throw Invalid();
        var expected = new Dictionary<LineIdentity, bool>();
        for (var index = 0; index < ExpectedInputCount; index++)
        {
            var name = InputReportPrefix + index.ToString("D4", CultureInfo.InvariantCulture) + InputReportSuffix;
            MergeInput(ReadRows(Path.Combine(directory, name), maximumBytes), expected);
        }
        var actual = ReadRows(mergedPath, maximumBytes);
        AssertExactRows(expected, actual);
    }

    private static Dictionary<LineIdentity, bool> ReadRows(string path, long maximumBytes)
    {
        var before = new FileInfo(path);
        if (!before.Exists || before.Length <= 0 || before.Length > maximumBytes || before.LinkTarget is not null)
        {
            throw Invalid();
        }
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length != before.Length)
        {
            throw Invalid();
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumBytes
        };
        using var reader = XmlReader.Create(stream, settings);
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        var root = document.DocumentElement;
        if (root is null || root.LocalName != CoverageRootName)
        {
            throw Invalid();
        }
        var rows = new Dictionary<LineIdentity, bool>();
        ReadPackages(root, rows);
        var after = new FileInfo(path);
        if (rows.Count == 0 || stream.Length != before.Length || after.Length != before.Length
            || after.LastWriteTimeUtc != before.LastWriteTimeUtc)
        {
            throw Invalid();
        }
        return rows;
    }

    private static void ReadPackages(XmlElement root, Dictionary<LineIdentity, bool> rows)
    {
        foreach (XmlNode packageNode in RequiredNodes(root, "./" + PackagesElementName + "/" + PackageElementName))
        {
            var package = RequiredAttribute(packageNode, PackageNameAttribute);
            foreach (XmlNode classNode in RequiredNodes(packageNode,
                "./" + ClassesElementName + "/" + ClassElementName))
            {
                ReadClass(package, classNode, rows);
            }
        }
    }

    private static void ReadClass(string package, XmlNode classNode, Dictionary<LineIdentity, bool> rows)
    {
        if (string.IsNullOrWhiteSpace(RequiredAttribute(classNode, ClassNameAttribute)))
        {
            throw Invalid();
        }
        var file = RequiredAttribute(classNode, FileNameAttribute);
        foreach (XmlNode lineNode in RequiredNodes(classNode, "./" + LinesElementName + "/" + LineElementName))
        {
            var line = ParsePositive(RequiredAttribute(lineNode, NumberAttribute));
            var covered = ParseCount(RequiredAttribute(lineNode, HitsAttribute)) > 0;
            MergeLine(rows, new(package, file, line), covered);
        }
    }

    private static void MergeInput(Dictionary<LineIdentity, bool> input, Dictionary<LineIdentity, bool> expected)
    {
        foreach (var row in input)
        {
            if (row.Value)
            {
                expected[row.Key] = true;
            }
            else
            {
                expected.TryAdd(row.Key, false);
            }
        }
    }

    private static void MergeLine(Dictionary<LineIdentity, bool> rows, LineIdentity key, bool covered)
    {
        if (covered)
        {
            rows[key] = true;
        }
        else
        {
            rows.TryAdd(key, false);
        }
    }

    private static void AssertExactRows(Dictionary<LineIdentity, bool> expected,
        Dictionary<LineIdentity, bool> actual)
    {
        if (expected.Count != actual.Count)
        {
            throw Invalid();
        }
        foreach (var row in expected)
        {
            if (!actual.TryGetValue(row.Key, out var covered) || covered != row.Value)
            {
                throw Invalid();
            }
        }
    }

    private static XmlNodeList RequiredNodes(XmlNode parent, string xpath)
        => parent.SelectNodes(xpath) ?? throw Invalid();

    private static string RequiredAttribute(XmlNode node, string name)
    {
        var value = node.Attributes?[name]?.Value;
        return string.IsNullOrWhiteSpace(value) ? throw Invalid() : value;
    }

    private static int ParsePositive(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed : throw Invalid();

    private static long ParseCount(string value)
        => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed : throw Invalid();

    private static InvalidDataException Invalid() => new(InvalidReport);

    private readonly record struct LineIdentity(string Module, string File, int Line);
}
