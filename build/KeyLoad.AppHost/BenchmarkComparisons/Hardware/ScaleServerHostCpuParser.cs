using System.Globalization;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerHostCpuParser
{
    private const string VendorField = "vendor_id";
    private const string FamilyField = "cpu family";
    private const string ModelField = "model";
    private const string SteppingField = "stepping";
    private const string ProcessorField = "processor";
    private const string PhysicalIdField = "physical id";
    private const string CoreIdField = "core id";

    internal static (string Vendor, int Family, int Model, int Stepping)? ParseCpu(string text)
    {
        const char LineFeedCharacter = '\n';
        const char ColonCharacter = ':';
        const int BoundaryValue = 1;
        const int SecondIndex = 1;
        const char SpaceCharacter = ' ';

        string? vendor = null;
        string? family = null;
        string? model = null;
        string? stepping = null;
        foreach (var line in text.Split(LineFeedCharacter))
        {
            var separator = line.IndexOf(ColonCharacter, StringComparison.Ordinal);
            if (separator < BoundaryValue)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + SecondIndex)..].Trim().Split(SpaceCharacter, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (value is null)
            {
                continue;
            }

            var accepted = name switch
            {
                VendorField => SetConsistent(ref vendor, value),
                FamilyField => SetConsistent(ref family, value),
                ModelField => SetConsistent(ref model, value),
                SteppingField => SetConsistent(ref stepping, value),
                _ => true
            };
            if (!accepted)
            {
                return null;
            }
        }

        return vendor is null || !int.TryParse(family, NumberStyles.None, CultureInfo.InvariantCulture, out var familyValue)
            || !int.TryParse(model, NumberStyles.None, CultureInfo.InvariantCulture, out var modelValue)
            || !int.TryParse(stepping, NumberStyles.None, CultureInfo.InvariantCulture, out var steppingValue)
            ? null : (vendor, familyValue, modelValue, steppingValue);
    }

    private static bool SetConsistent(ref string? current, string value)
    {
        if (current is not null)
        {
            return string.Equals(current, value, StringComparison.Ordinal);
        }

        current = value;
        return true;
    }

    internal static string? ReadValue(string text, string name)
    {
        const char LineFeedCharacter = '\n';
        const char ColonCharacter = ':';
        const int BoundaryValue = 0;
        const int SecondIndex = 1;
        const char SpaceCharacter = ' ';

        foreach (var line in text.Split(LineFeedCharacter))
        {
            var separator = line.IndexOf(ColonCharacter, StringComparison.Ordinal);
            if (separator > BoundaryValue && line[..separator].Trim().Equals(name, StringComparison.Ordinal))
            {
                return line[(separator + SecondIndex)..].Trim().Split(SpaceCharacter, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
        }

        return null;
    }

    internal static int CountOnline(string text)
    {
        const int TotalInitialValue = 0;
        const char SeparatorCharacter = ',';
        const char CountOnlineSeparatorCharacter = '-';
        const int FirstIndex = 0;
        const int EmptyResult = 0;
        const int RangeEndpointCount = 2;
        const int SecondIndex = 1;
        const int Step = 1;

        var total = TotalInitialValue;
        foreach (var range in text.Trim().Split(SeparatorCharacter))
        {
            var ends = range.Split(CountOnlineSeparatorCharacter);
            if (!int.TryParse(ends[FirstIndex], out var first))
            {
                return EmptyResult;
            }

            var last = first;
            if (ends.Length == RangeEndpointCount && !int.TryParse(ends[SecondIndex], out last))
            {
                return EmptyResult;
            }

            total = checked(total + last - first + Step);
        }

        return total;
    }

    internal static (string Logical, string[] Physical) CoreMembership(string cpu)
    {
        const char LineFeedCharacter = '\n';
        const char ColonCharacter = ':';
        const int BoundaryValue = 1;
        const int SecondIndex = 1;
        const int NoCores = 0;
        const char SeparatorCharacter = ',';

        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var logical = new HashSet<string>(StringComparer.Ordinal);
        string? physical = null;
        string? core = null;
        string? processor = null;
        foreach (var line in cpu.Split(LineFeedCharacter).Append(string.Empty))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddCoreMembership(pairs, logical, physical, core, processor);
                physical = null;
                core = null;
                processor = null;
                continue;
            }

            var separator = line.IndexOf(ColonCharacter, StringComparison.Ordinal);
            if (separator < BoundaryValue)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + SecondIndex)..].Trim();
            if (name == PhysicalIdField)
            {
                physical = value;
            }

            if (name == CoreIdField)
            {
                core = value;
            }

            if (name == ProcessorField)
            {
                processor = value;
            }
        }

        return pairs.Count == NoCores ? (string.Empty, []) :
            (string.Join(SeparatorCharacter, logical.Order(StringComparer.Ordinal)), pairs.Order(StringComparer.Ordinal).ToArray());
    }

    private static void AddCoreMembership(HashSet<string> pairs, HashSet<string> logical,
        string? physical, string? core, string? processor)
    {
        const string ItemText = ":";

        if (physical is not null && core is not null)
        {
            pairs.Add(physical + ItemText + core);
        }

        if (processor is not null)
        {
            logical.Add(processor);
        }
    }
}
