using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// BuildMind canonical JSON v1 ("BM-JCS-1"): RFC 8785 (JCS) restricted to a number domain where .NET and Python
/// produce identical text. Mirrors <c>rag-service/app/canonical_json.py</c>; both are checked against the shared vectors
/// in <c>rag-service/contract/canonical_json_vectors.v1.json</c>.
/// <list type="bullet">
/// <item>Objects: members sorted by UTF-16 code units (ordinal), no whitespace, duplicates rejected.</item>
/// <item>Strings: escape only <c>"</c>, <c>\</c> and U+0000..U+001F (short forms for \b \t \n \f \r, otherwise
/// <c>\u00xx</c> lowercase); lone surrogates rejected; everything else is emitted as UTF-8.</item>
/// <item>Numbers: finite; integral values with |x| &lt; 1e15 as plain integers (-0 becomes 0); non-integral values
/// with 1e-4 &lt;= |x| &lt; 1e15 as the shortest round-trip decimal. Anything else is rejected.</item>
/// </list>
/// </summary>
public static class CanonicalJson
{
    public const string Algorithm = "SHA-256/BM-JCS-1";
    private const double Max = 1e15;
    private const double MinFraction = 1e-4;

    /// <summary>Canonical text, or null when the value is outside the canonical domain (never a best-effort result).</summary>
    public static string? TryCanonicalize(JsonElement element)
    {
        try
        {
            var builder = new StringBuilder();
            return Write(element, builder) ? builder.ToString() : null;
        }
        catch (InvalidOperationException)
        {
            return null; // e.g. an escaped lone surrogate that cannot be transcoded
        }
    }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 canonical text, or null when it cannot be canonicalized.</summary>
    public static string? TrySha256(JsonElement element, string? excludeRootMember = null)
    {
        try
        {
            var builder = new StringBuilder();
            var ok = element.ValueKind == JsonValueKind.Object && excludeRootMember is not null
                ? WriteObject(element, builder, excludeRootMember)
                : Write(element, builder);
            return ok ? Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))) : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static bool Write(JsonElement element, StringBuilder output) => element.ValueKind switch
    {
        JsonValueKind.Null => Append(output, "null"),
        JsonValueKind.True => Append(output, "true"),
        JsonValueKind.False => Append(output, "false"),
        JsonValueKind.Number => WriteNumber(element, output),
        JsonValueKind.String => WriteString(element.GetString()!, output),
        JsonValueKind.Array => WriteArray(element, output),
        JsonValueKind.Object => WriteObject(element, output, exclude: null),
        _ => false,
    };

    private static bool Append(StringBuilder output, string text)
    {
        output.Append(text);
        return true;
    }

    private static bool WriteArray(JsonElement element, StringBuilder output)
    {
        output.Append('[');
        var first = true;
        foreach (var item in element.EnumerateArray())
        {
            if (!first)
            {
                output.Append(',');
            }

            first = false;
            if (!Write(item, output))
            {
                return false;
            }
        }

        output.Append(']');
        return true;
    }

    private static bool WriteObject(JsonElement element, StringBuilder output, string? exclude)
    {
        var members = element.EnumerateObject().Where(p => p.Name != exclude).ToList();
        members.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); // ordinal == UTF-16 code unit order
        for (var i = 1; i < members.Count; i++)
        {
            if (members[i].Name == members[i - 1].Name)
            {
                return false; // duplicate member
            }
        }

        output.Append('{');
        for (var i = 0; i < members.Count; i++)
        {
            if (i > 0)
            {
                output.Append(',');
            }

            if (!WriteString(members[i].Name, output))
            {
                return false;
            }

            output.Append(':');
            if (!Write(members[i].Value, output))
            {
                return false;
            }
        }

        output.Append('}');
        return true;
    }

    private static bool WriteNumber(JsonElement element, StringBuilder output)
    {
        if (!element.TryGetDouble(out var x) || !double.IsFinite(x) || Math.Abs(x) >= Max)
        {
            return false;
        }

        if (x == Math.Truncate(x))
        {
            output.Append(((long)x).ToString(CultureInfo.InvariantCulture)); // also maps -0 to "0"
            return true;
        }

        if (Math.Abs(x) < MinFraction)
        {
            return false;
        }

        var text = x.ToString("R", CultureInfo.InvariantCulture);
        if (text.Contains('E', StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        output.Append(text);
        return true;
    }

    private static bool WriteString(string value, StringBuilder output)
    {
        output.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '"': output.Append("\\\""); break;
                case '\\': output.Append("\\\\"); break;
                case '\b': output.Append("\\b"); break;
                case '\t': output.Append("\\t"); break;
                case '\n': output.Append("\\n"); break;
                case '\f': output.Append("\\f"); break;
                case '\r': output.Append("\\r"); break;
                default:
                    if (c < 0x20)
                    {
                        output.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        output.Append(c).Append(value[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        return false; // lone surrogate
                    }
                    else
                    {
                        output.Append(c);
                    }

                    break;
            }
        }

        output.Append('"');
        return true;
    }
}
