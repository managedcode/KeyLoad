using System.Globalization;

namespace KeyLoad.Comparisons;

/// <summary>Admits the exact document family selectors before native resource allocation.</summary>
public static class DocumentWorkerSelection
{
    /// <summary>Names the actual document operation selector.</summary>
    public const string ScenarioSetting = "Benchmarks:DocumentScenario";
    /// <summary>Names the initial corpus or ingestion record count.</summary>
    public const string RecordsSetting = "Benchmarks:DocumentRecords";
    /// <summary>Names independently owned native clients.</summary>
    public const string ClientsSetting = "Benchmarks:DocumentClients";
    private const string InvalidSelection = "DocumentComparisonSelectionInvalid";
    private const int MillionRecords = 1_000_000;
    private const string HundredThousand = "100k";
    private const string Million = "1m";
    private const string Prefix = "documents-";
    private const string Separator = "-";
    private const string ClientsPrefix = "-c";
    private const string SequentialReadIdentity = "sequential-read";
    private const string RandomReadIdentity = "random-read";
    private const string CreateIdentity = "create";
    private const string UpdateIdentity = "update";
    private const string DeleteIdentity = "delete";
    private const string ReadUpdate50Identity = "read-update50";
    private const string ReadUpdate95Identity = "read-update95";
    private const string MixedCrudIdentity = "mixed-crud";
    private const string IngestIdentity = "ingest";

    internal static DocumentComparisonSelection? Read(ComparisonWorkerSelectionOptions options)
    {
        if (options.DocumentScenario is null && options.DocumentRecords is null && options.DocumentClients is null)
        {
            return null;
        }
        if (!Enum.TryParse<DocumentComparisonScenario>(options.DocumentScenario, out var scenario)
            || !Enum.IsDefined(scenario) || scenario.ToString() != options.DocumentScenario
            || !int.TryParse(options.DocumentRecords, NumberStyles.None, CultureInfo.InvariantCulture, out var records)
            || !int.TryParse(options.DocumentClients, NumberStyles.None, CultureInfo.InvariantCulture, out var clients))
        {
            throw new InvalidOperationException(InvalidSelection);
        }
        var selection = new DocumentComparisonSelection(scenario, records, clients);
        selection.Validate();
        return selection;
    }

    /// <summary>Returns the source-bound family/profile identity for one complete native workload.</summary>
    public static string ProfileId(DocumentComparisonSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        var size = selection.DatasetRecords == MillionRecords ? Million : HundredThousand;
        return Prefix + size + Separator + ScenarioId(selection.Scenario) + ClientsPrefix
            + selection.Clients.ToString(CultureInfo.InvariantCulture);
    }

    private static string ScenarioId(DocumentComparisonScenario scenario) => scenario switch
    {
        DocumentComparisonScenario.SequentialRead => SequentialReadIdentity,
        DocumentComparisonScenario.RandomRead => RandomReadIdentity,
        DocumentComparisonScenario.Create => CreateIdentity,
        DocumentComparisonScenario.Update => UpdateIdentity,
        DocumentComparisonScenario.Delete => DeleteIdentity,
        DocumentComparisonScenario.ReadUpdate50 => ReadUpdate50Identity,
        DocumentComparisonScenario.ReadUpdate95 => ReadUpdate95Identity,
        DocumentComparisonScenario.MixedCrud => MixedCrudIdentity,
        DocumentComparisonScenario.Ingest => IngestIdentity,
        _ => throw new InvalidOperationException(InvalidSelection)
    };
}
