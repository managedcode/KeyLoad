namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Explicit functional-only admission for the exclusive million-record CLUSTER case.</summary>
[ConfigurationOptions]
internal sealed class HeavyDocumentLoadExecutionOptions
{
    internal const string SectionName = "KeyLoadTests:HeavyLoad";
    internal const string EnabledSetting = SectionName + ":Enabled";
    internal const string ValidationMessage = "Heavy CLUSTER load requires explicit exclusive functional admission.";
    public bool Enabled
    {
        get; set;
    }
}
