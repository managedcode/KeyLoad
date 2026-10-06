using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;

namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoberturaBranchPairAssertions
{
    private const string BranchesValidAttribute = "branches-valid";
    private const string BranchesCoveredAttribute = "branches-covered";
    private const string BranchAttribute = "branch";
    private const string ConditionCoverageAttribute = "condition-coverage";
    private const string NativeBranchPairsProperty = "nativeCoberturaBranchPairs";
    private const string ValidProperty = "valid";
    private const string CoveredProperty = "covered";
    private const string BranchCoveragePattern = @"\A(?:100(?:\.0{1,2})?|(?:0|[1-9][0-9]?)(?:\.[0-9]{1,2})?)% \(([0-9]+)/([0-9]+)\)\z";
    private const string InvalidReport = "The native Cobertura branch aggregate is invalid.";

    internal static async Task AssertMatchesAsync(XmlElement root, JsonElement proof)
    {
        var observedRows = ReadBranchPairs(root);
        var rootCounts = ReadRootBranchPairs(root);
        var actual = proof.GetProperty(NativeBranchPairsProperty);
        if (rootCounts is null)
        {
            await Assert.That(actual.ValueKind).IsEqualTo(JsonValueKind.Null);
            return;
        }
        if (rootCounts.Value != observedRows)
        {
            throw new InvalidDataException(InvalidReport);
        }
        await Assert.That(actual.GetProperty(ValidProperty).GetInt64()).IsEqualTo(rootCounts.Value.Valid);
        await Assert.That(actual.GetProperty(CoveredProperty).GetInt64()).IsEqualTo(rootCounts.Value.Covered);
    }

    private static BranchPairCounts ReadBranchPairs(XmlElement root)
    {
        long valid = 0;
        long covered = 0;
        foreach (XmlNode node in root.SelectNodes("./packages/package/classes/class/lines/line")
            ?? throw new InvalidDataException(InvalidReport))
        {
            var line = (XmlElement)node;
            var branch = line.GetAttribute(BranchAttribute);
            if (branch.Length == 0 || string.Equals(branch, "false", StringComparison.OrdinalIgnoreCase))
            { continue; }
            if (!string.Equals(branch, "true", StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException(InvalidReport); }
            var match = Regex.Match(line.GetAttribute(ConditionCoverageAttribute), BranchCoveragePattern);
            if (!match.Success)
            { throw new InvalidDataException(InvalidReport); }
            var lineCovered = ParseCount(match.Groups[1].Value);
            var lineValid = ParseCount(match.Groups[2].Value);
            if (lineValid == 0 || lineCovered > lineValid)
            { throw new InvalidDataException(InvalidReport); }
            valid = checked(valid + lineValid);
            covered = checked(covered + lineCovered);
        }
        return new BranchPairCounts(valid, covered);
    }

    private static BranchPairCounts? ReadRootBranchPairs(XmlElement root)
    {
        var hasValid = root.HasAttribute(BranchesValidAttribute);
        var hasCovered = root.HasAttribute(BranchesCoveredAttribute);
        if (hasValid != hasCovered)
        {
            throw new InvalidDataException(InvalidReport);
        }
        if (!hasValid)
        {
            return null;
        }
        var valid = ParseCount(root.GetAttribute(BranchesValidAttribute));
        var covered = ParseCount(root.GetAttribute(BranchesCoveredAttribute));
        if (covered > valid)
        {
            throw new InvalidDataException(InvalidReport);
        }
        return new BranchPairCounts(valid, covered);
    }

    private static long ParseCount(string? value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < 0)
        { throw new InvalidDataException(InvalidReport); }
        return result;
    }

    private readonly record struct BranchPairCounts(long Valid, long Covered);
}
