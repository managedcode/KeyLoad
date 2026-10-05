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
        string? vendor = null;
        string? family = null;
        string? model = null;
        string? stepping = null;
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 1)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
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
        foreach (var line in text.Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0 && line[..separator].Trim().Equals(name, StringComparison.Ordinal))
            {
                return line[(separator + 1)..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            }
        }

        return null;
    }

    internal static int CountOnline(string text)
    {
        var total = 0;
        foreach (var range in text.Trim().Split(','))
        {
            var ends = range.Split('-');
            if (!int.TryParse(ends[0], out var first))
            {
                return 0;
            }

            var last = first;
            if (ends.Length == 2 && !int.TryParse(ends[1], out last))
            {
                return 0;
            }

            total = checked(total + last - first + 1);
        }

        return total;
    }

    internal static (string Logical, string[] Physical) CoreMembership(string cpu)
    {
        var pairs = new HashSet<string>(StringComparer.Ordinal);
        var logical = new HashSet<string>(StringComparer.Ordinal);
        string? physical = null;
        string? core = null;
        string? processor = null;
        foreach (var line in cpu.Split('\n').Append(string.Empty))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                AddCoreMembership(pairs, logical, physical, core, processor);
                physical = null;
                core = null;
                processor = null;
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator < 1)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
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

        return pairs.Count == 0 ? (string.Empty, []) :
            (string.Join(',', logical.Order(StringComparer.Ordinal)), pairs.Order(StringComparer.Ordinal).ToArray());
    }

    private static void AddCoreMembership(HashSet<string> pairs, HashSet<string> logical,
        string? physical, string? core, string? processor)
    {
        if (physical is not null && core is not null)
        {
            pairs.Add(physical + ":" + core);
        }

        if (processor is not null)
        {
            logical.Add(processor);
        }
    }
}
