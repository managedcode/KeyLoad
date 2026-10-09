using System.ComponentModel;
using System.Globalization;

namespace KeyLoad.Comparisons;

internal static class ComparisonTopologyNames
{
    internal const string Single = "Single";
}

/// <summary>Preserves the comparison topology's established configuration names for external type-descriptor callers.</summary>
public sealed class ComparisonTopologyConverter : EnumConverter
{
    private const char ListSeparator = ',';

    /// <summary>Creates the topology converter used by the enum's configuration metadata.</summary>
    public ComparisonTopologyConverter() : base(typeof(ComparisonTopology))
    {
    }

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        var mapped = string.Join(ListSeparator, text.Split(ListSeparator).Select(MapCanonicalName));
        return base.ConvertFrom(context, culture, mapped);
    }

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is ComparisonTopology.Standalone)
        {
            return ComparisonTopologyNames.Single;
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    private static string MapCanonicalName(string token)
    {
        var name = token.Trim();
        if (!name.Equals(ComparisonTopologyNames.Single, StringComparison.OrdinalIgnoreCase))
        {
            return token;
        }

        var leading = token.Length - token.TrimStart().Length;
        var trailing = token.Length - token.TrimEnd().Length;
        return $"{token[..leading]}{nameof(ComparisonTopology.Standalone)}{token[(token.Length - trailing)..]}";
    }
}
