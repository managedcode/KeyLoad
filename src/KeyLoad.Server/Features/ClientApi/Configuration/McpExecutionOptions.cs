using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Bounds private MCP framing and retained work without changing public protocol identities.</summary>
[ConfigurationOptions]
internal sealed class McpExecutionOptions
{
    internal const string SectionName = "KeyLoad:McpExecution";
    internal const string ValidationMessage = "MCP framing, reply and workspace limits must be positive and within their accepted ceilings.";
    private const int MaximumCatalogOperationsCeiling = 256;
    private const int MaximumCatalogMetadataBytesCeiling = 4_194_304;
    private const int MaximumDescriptorCharactersCeiling = 4_096;
    private const int MaximumOperationNameCharactersCeiling = 64;
    private const int MaximumAliasCharactersCeiling = 128;
    private const int MaximumAliasesPerOperationCeiling = 4;
    private const int MaximumSearchResultsCeiling = 4;
    private const int DefaultSearchResultsCeiling = 3;
    private const int MaximumDiscoveryQueryBytesCeiling = 2_048;
    private const int MaximumRouteResultsCeiling = 2;
    private const int DefaultRouteResultsCeiling = 2;
    private const int MaximumAgentGuideBytesCeiling = 8_192;
    private const int MaximumTransportMethodCharactersCeiling = 64;
    private const int MaximumTransportNameCharactersCeiling = 256;
    private const int MinimumPositiveBudget = 1;
    private const int NativeReplyWrapperDepth = 3;
    private const int MaximumDepthCeiling = 64;
    private const int MaximumReplyDepthCeiling = 61;
    private const int MaximumIdentifierByteCeiling = 256;
    private const int MaximumTokenCeiling = 131_072;
    private const int MaximumPropertyCeiling = 32_768;
    private const int MaximumPropertyNameByteCeiling = 256;
    private const int MaximumDataReplyByteCeiling = 16_777_216;
    private const int MaximumControlReplyByteCeiling = 65_536;
    private const int MaximumEnvelopeByteCeiling = 65_536;
    private const int MaximumRequestByteCeiling = 33_554_432;
    private const int MaximumScratchByteCeiling = 16_384;
    private const int MaximumFailureByteCeiling = 262_144;
    private const int MaximumEnvelopeItemCeiling = 1_024;

    public int MaximumDepth { get; set; } = MaximumDepthCeiling;
    public int MaximumReplyDepth { get; set; } = MaximumReplyDepthCeiling;
    public int MaximumIdentifierBytes { get; set; } = MaximumIdentifierByteCeiling;
    public int MaximumTokens { get; set; } = MaximumTokenCeiling;
    public int MaximumProperties { get; set; } = MaximumPropertyCeiling;
    public int MaximumPropertyNameBytes { get; set; } = MaximumPropertyNameByteCeiling;
    public int MaximumDataReplyBytes { get; set; } = MaximumDataReplyByteCeiling;
    public int MaximumControlReplyBytes { get; set; } = MaximumControlReplyByteCeiling;
    public int EnvelopeAllowanceBytes { get; set; } = MaximumEnvelopeByteCeiling;
    public int MaximumAuthenticationBytes { get; set; } = MaximumDataReplyByteCeiling;
    public int MaximumRequestCapacityBytes { get; set; } = MaximumRequestByteCeiling;
    public int MaximumCanonicalPayloadBytes { get; set; } = MaximumRequestByteCeiling;
    public int IngressScratchBytes { get; set; } = MaximumScratchByteCeiling;
    public int SafeFailureBytes { get; set; } = MaximumFailureByteCeiling;
    public int NativeEnvelopeItems { get; set; } = MaximumEnvelopeItemCeiling;

    public int MaximumCatalogOperations { get; set; } = MaximumCatalogOperationsCeiling;
    public int MaximumCatalogMetadataBytes { get; set; } = MaximumCatalogMetadataBytesCeiling;
    public int MaximumDescriptorCharacters { get; set; } = MaximumDescriptorCharactersCeiling;
    public int MaximumOperationNameCharacters { get; set; } = MaximumOperationNameCharactersCeiling;
    public int MaximumAliasCharacters { get; set; } = MaximumAliasCharactersCeiling;
    public int MaximumAliasesPerOperation { get; set; } = MaximumAliasesPerOperationCeiling;
    public int MaximumSearchResults { get; set; } = MaximumSearchResultsCeiling;
    public int DefaultSearchResults { get; set; } = DefaultSearchResultsCeiling;
    public int MaximumDiscoveryQueryBytes { get; set; } = MaximumDiscoveryQueryBytesCeiling;
    public int MaximumRouteResults { get; set; } = MaximumRouteResultsCeiling;
    public int DefaultRouteResults { get; set; } = DefaultRouteResultsCeiling;
    public int MaximumAgentGuideBytes { get; set; } = MaximumAgentGuideBytesCeiling;
    public int MaximumTransportMethodCharacters { get; set; } = MaximumTransportMethodCharactersCeiling;
    public int MaximumTransportNameCharacters { get; set; } = MaximumTransportNameCharactersCeiling;

    internal bool IsValid() => Bounded(MaximumDepth, MaximumDepthCeiling)
        && Bounded(MaximumReplyDepth, MaximumReplyDepthCeiling)
        && MaximumReplyDepth <= MaximumDepth - NativeReplyWrapperDepth
        && Bounded(MaximumIdentifierBytes, MaximumIdentifierByteCeiling)
        && Bounded(MaximumTokens, MaximumTokenCeiling) && Bounded(MaximumProperties, MaximumPropertyCeiling)
        && MaximumProperties <= MaximumTokens
        && Bounded(MaximumPropertyNameBytes, MaximumPropertyNameByteCeiling)
        && Bounded(MaximumDataReplyBytes, MaximumDataReplyByteCeiling)
        && Bounded(MaximumControlReplyBytes, MaximumControlReplyByteCeiling)
        && MaximumControlReplyBytes <= MaximumDataReplyBytes
        && Bounded(EnvelopeAllowanceBytes, MaximumEnvelopeByteCeiling)
        && Bounded(MaximumAuthenticationBytes, MaximumDataReplyByteCeiling)
        && Bounded(MaximumRequestCapacityBytes, MaximumRequestByteCeiling)
        && Bounded(MaximumCanonicalPayloadBytes, MaximumRequestByteCeiling)
        && MaximumCanonicalPayloadBytes <= MaximumRequestCapacityBytes
        && Bounded(IngressScratchBytes, MaximumScratchByteCeiling)
        && Bounded(SafeFailureBytes, MaximumFailureByteCeiling)
        && Bounded(NativeEnvelopeItems, MaximumEnvelopeItemCeiling)
        && Bounded(MaximumCatalogOperations, MaximumCatalogOperationsCeiling)
        && Bounded(MaximumCatalogMetadataBytes, MaximumCatalogMetadataBytesCeiling)
        && Bounded(MaximumDescriptorCharacters, MaximumDescriptorCharactersCeiling)
        && Bounded(MaximumOperationNameCharacters, MaximumOperationNameCharactersCeiling)
        && Bounded(MaximumAliasCharacters, MaximumAliasCharactersCeiling)
        && Bounded(MaximumAliasesPerOperation, MaximumAliasesPerOperationCeiling)
        && Bounded(MaximumSearchResults, MaximumSearchResultsCeiling)
        && Bounded(DefaultSearchResults, DefaultSearchResultsCeiling)
        && Bounded(MaximumDiscoveryQueryBytes, MaximumDiscoveryQueryBytesCeiling)
        && Bounded(MaximumRouteResults, MaximumRouteResultsCeiling)
        && Bounded(DefaultRouteResults, DefaultRouteResultsCeiling)
        && Bounded(MaximumAgentGuideBytes, MaximumAgentGuideBytesCeiling)
        && Bounded(MaximumTransportMethodCharacters, MaximumTransportMethodCharactersCeiling)
        && Bounded(MaximumTransportNameCharacters, MaximumTransportNameCharactersCeiling)
        && DefaultSearchResults <= MaximumSearchResults && DefaultRouteResults <= MaximumRouteResults;

    internal void Validate()
    {
        if (!IsValid())
        { throw new OptionsValidationException(SectionName, typeof(McpExecutionOptions), [ValidationMessage]); }
    }

    private static bool Bounded(int value, int maximum) => value >= MinimumPositiveBudget && value <= maximum;
}
