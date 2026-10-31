using System.Xml.Linq;

namespace KeyLoad.WebsiteTests.BenchmarkComparisons;

/// <summary>Reads the comparison and recorded-values tables of one data type from a built index.html.</summary>
internal sealed class SiteResultsPage(string html)
{
    private const string SectionEnd = "</section>";

    internal string Html { get; } = html;

    internal static async Task<SiteResultsPage> ReadAsync(string site, CancellationToken cancellationToken)
        => new(await File.ReadAllTextAsync(Path.Combine(site, SiteTokens.IndexFile), cancellationToken));

    // Each type section is well-formed markup without nested sections, so it parses as XML on its own.
    internal XElement Section(string type)
    {
        var start = Html.IndexOf($"<section class=\"bench-panel bench-overview\" id=\"bench-{type}\"", StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidDataException($"The {type} results section is missing.");
        }

        var end = Html.IndexOf(SectionEnd, start, StringComparison.Ordinal) + SectionEnd.Length;
        return XElement.Parse(Html[start..end]);
    }

    internal static string Title(XElement section) => section.Descendants("h3").Single().Value;

    internal static string[] Columns(XElement section) => [.. Comparison(section).Descendants("thead").Single()
        .Descendants("th").Select(cell => Attribute(cell, "data-case")).Where(value => value.Length > 0)];

    internal static SiteTableRow[] Rows(XElement section) => [.. Comparison(section).Descendants("tr")
        .Where(row => HasClass(row, "ov-row")).Select(row => new SiteTableRow(Attribute(row, "data-database"),
            row.Element("th")!.Descendants("small").SingleOrDefault()?.Value ?? string.Empty,
            row.Elements("td").ToDictionary(cell => Attribute(cell, "data-case"), Lines)))];

    internal static SiteTableRow Row(XElement section, string database) => Rows(section).Single(row => row.Database == database);

    internal static XElement Recorded(XElement section, string database, int nodes, string caseId) => section
        .Descendants("details").Single().Descendants("tbody").Single().Elements("tr").Single(row =>
            Attribute(row, "data-database") == database && Attribute(row, "data-nodes") == nodes.ToString(
                System.Globalization.CultureInfo.InvariantCulture) && Attribute(row, "data-case") == caseId);

    internal static XElement Field(XElement recorded, string field)
        => recorded.Elements("td").Single(cell => Attribute(cell, "data-field") == field);

    internal static string ErrorText(XElement recorded)
        => recorded.Elements("td").Single(cell => Attribute(cell, "class") == "error-text").Value;

    internal static string Attribute(XElement element, string name) => (string?)element.Attribute(name) ?? string.Empty;

    private static XElement Comparison(XElement section)
        => section.Descendants("table").Single(table => HasClass(table, "comparison-grid"));

    private static SiteTableLine[] Lines(XElement cell) => [.. cell.Descendants("span")
        .Where(line => HasClass(line, "pair-line")).Select(line => new SiteTableLine(
            int.Parse(Attribute(line, "data-nodes"), System.Globalization.CultureInfo.InvariantCulture),
            Attribute(line, "data-status"), Attribute(line, "data-value"), line.Value))];

    private static bool HasClass(XElement element, string name)
        => Attribute(element, "class").Split(' ').Contains(name, StringComparer.Ordinal);
}
