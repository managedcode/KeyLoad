namespace KeyLoad;

/// <summary>One owned opaque page; the caller never receives a execution storage handle.</summary>
[Orleans.GenerateSerializer]
[Orleans.Alias(RuntimeJournalAliases.Page)]
public sealed record RuntimeJournalPage(
    [property: Orleans.Id(0)] ReadOnlyMemory<byte> Data,
    [property: Orleans.Id(1)] bool IsCompleted);
