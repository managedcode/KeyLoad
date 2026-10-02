using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class MachineKeyRuleCatalog
{
    public static ImmutableHashSet<string> AllStringArgumentAttributes { get; } =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            MachineKeyAttributeNames.Alias,
            MachineKeyAttributeNames.AliasAttribute,
            MachineKeyAttributeNames.ConfigurationKeyName,
            MachineKeyAttributeNames.ConfigurationKeyNameAttribute,
            MachineKeyAttributeNames.JsonDerivedType,
            MachineKeyAttributeNames.JsonDerivedTypeAttribute,
            MachineKeyAttributeNames.JsonPropertyName,
            MachineKeyAttributeNames.JsonPropertyNameAttribute,
            MachineKeyAttributeNames.PersistentState,
            MachineKeyAttributeNames.PersistentStateAttribute,
            MachineKeyAttributeNames.StorageProvider,
            MachineKeyAttributeNames.StorageProviderAttribute);

    public static ImmutableDictionary<string, ImmutableHashSet<string>> NamedStringArgumentAttributes { get; } =
        new Dictionary<string, ImmutableHashSet<string>>(StringComparer.Ordinal)
        {
            [MachineKeyAttributeNames.BindProperty] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.BindPropertyAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.DataMember] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.DataMemberAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.FromForm] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.FromFormAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.FromHeader] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.FromHeaderAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.FromQuery] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.FromQueryAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.JsonPolymorphic] =
                Names(MachineKeyAttributeArgumentNames.TypeDiscriminatorPropertyName),
            [MachineKeyAttributeNames.JsonPolymorphicAttribute] =
                Names(MachineKeyAttributeArgumentNames.TypeDiscriminatorPropertyName),
            [MachineKeyAttributeNames.JsonProperty] = Names(MachineKeyAttributeArgumentNames.PropertyName),
            [MachineKeyAttributeNames.JsonPropertyAttribute] =
                Names(MachineKeyAttributeArgumentNames.PropertyName),
            [MachineKeyAttributeNames.McpServerPrompt] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.McpServerPromptAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.McpServerResource] = Names(
                MachineKeyAttributeArgumentNames.Name,
                MachineKeyAttributeArgumentNames.UriTemplate),
            [MachineKeyAttributeNames.McpServerResourceAttribute] = Names(
                MachineKeyAttributeArgumentNames.Name,
                MachineKeyAttributeArgumentNames.UriTemplate),
            [MachineKeyAttributeNames.McpServerTool] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.McpServerToolAttribute] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.SupplyParameterFromQuery] = Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.SupplyParameterFromQueryAttribute] =
                Names(MachineKeyAttributeArgumentNames.Name),
            [MachineKeyAttributeNames.YamlMember] = Names(MachineKeyAttributeArgumentNames.Alias),
            [MachineKeyAttributeNames.YamlMemberAttribute] = Names(MachineKeyAttributeArgumentNames.Alias),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public static ImmutableHashSet<string> AlwaysKeyMethodNames { get; } =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            MachineKeyMethodNames.ContainsKey,
            MachineKeyMethodNames.FindFirstValue,
            MachineKeyMethodNames.Get,
            MachineKeyMethodNames.GetBool,
            MachineKeyMethodNames.GetDuration,
            MachineKeyMethodNames.GetInt,
            MachineKeyMethodNames.GetLong,
            MachineKeyMethodNames.GetMany,
            MachineKeyMethodNames.GetProperty,
            MachineKeyMethodNames.HasFlag,
            MachineKeyMethodNames.TryGetProperty,
            MachineKeyMethodNames.TryGetValue,
            MachineKeyMethodNames.TryAddWithoutValidation,
            MachineKeyMethodNames.WithParameter,
            MachineKeyMethodNames.WriteBoolean,
            MachineKeyMethodNames.WriteNull,
            MachineKeyMethodNames.WriteNumber,
            MachineKeyMethodNames.WritePropertyName,
            MachineKeyMethodNames.WriteString,
            MachineKeyMethodNames.WithName,
            MachineKeyMethodNames.WithTags);

    public static ImmutableHashSet<string> ExplicitKeyParameterNames { get; } =
        ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
            MachineKeyParameterNames.ConfigurationKey,
            MachineKeyParameterNames.DictionaryKey,
            MachineKeyParameterNames.HeaderName,
            MachineKeyParameterNames.InputKey,
            MachineKeyParameterNames.ItemKey,
            MachineKeyParameterNames.Key,
            MachineKeyParameterNames.MetadataKey,
            MachineKeyParameterNames.OutputKey,
            MachineKeyParameterNames.ParameterName,
            MachineKeyParameterNames.PropertyName,
            MachineKeyParameterNames.QueryParameterName,
            MachineKeyParameterNames.RouteValueName,
            MachineKeyParameterNames.SourceKey,
            MachineKeyParameterNames.TagKey,
            MachineKeyParameterNames.TargetKey);

    private static ImmutableHashSet<string> Names(params string[] names) =>
        ImmutableHashSet.Create(StringComparer.Ordinal, names);
}
