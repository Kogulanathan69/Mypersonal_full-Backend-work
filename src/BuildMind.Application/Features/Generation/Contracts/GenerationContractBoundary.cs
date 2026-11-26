using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BuildMind.Application.Features.Generation.Contracts;

/// <summary>A safe local rejection with no submitted values or serializer context.</summary>
public sealed class GenerationContractException : Exception
{
    public GenerationContractException() : this("CONTRACT_VALUE_INVALID", "A submitted value violates the contract.") { }

    internal GenerationContractException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}

public sealed record GenerationContractIssue(string Code, string Message);

/// <summary>Opt-in boundary for the provisional M6 wire contracts.</summary>
public static partial class GenerationContractBoundary
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(GenerationContractJson.StrictOptions);
        options.Converters.Add(new UtcTimestampConverter());
        options.Converters.Add(new CanonicalGuidConverter());
        return options;
    }

    public static T Parse<T>(string payload) where T : class => (T)Parse(payload, typeof(T));

    public static object Parse(string payload, Type contractType)
    {
        using var document = ReadDocument(payload);
        try
        {
            CheckUniqueMembers(document.RootElement);
        }
        catch (InvalidOperationException)
        {
            throw InvalidJson();
        }
        try
        {
            var result = document.RootElement.Deserialize(contractType, Options);
            GenerationContractValidation.Validate(result);
            return result!;
        }
        catch (JsonException)
        {
            // Serializer errors may contain raw property names, values and paths.
            throw new GenerationContractException();
        }
    }

    private static JsonDocument ReadDocument(string payload)
    {
        try
        {
            return JsonDocument.Parse(payload);
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw InvalidJson();
        }
    }

    public static GenerationContractIssue FormatPublicIssue(Exception error)
        => error is GenerationContractException contract
            ? new(contract.Code, contract.Message)
            : new("CONTRACT_VALUE_INVALID", "A submitted value violates the contract.");

    private static GenerationContractException InvalidJson()
        => new("JSON_INVALID", "The request is not valid contract JSON.");

    private static void CheckUniqueMembers(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                        throw InvalidJson();
                    CheckUniqueMembers(property.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                    CheckUniqueMembers(item);
                break;
        }
    }

    private sealed class CanonicalGuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String || !reader.TryGetGuid(out var id) || id == Guid.Empty)
                throw new JsonException();
            return id;
        }

        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
            => writer.WriteStringValue(value);
    }

    [GeneratedRegex(@"\A[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,6})?(?:Z|[+-](?:[01][0-9]|2[0-3]):[0-5][0-9])\z", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    private sealed class UtcTimestampConverter : JsonConverter<DateTimeOffset>
    {
        private static readonly string[] LocalFormats = ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFF"];

        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException();
            var raw = reader.GetString();
            if (raw is null || raw.Length is < 20 or > 32 || !TimestampPattern().IsMatch(raw))
                throw new JsonException();

            var isZulu = raw[^1] == 'Z';
            var suffix = isZulu ? raw.Length - 1 : raw.Length - 6;
            if (!DateTime.TryParseExact(raw[..suffix], LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                throw new JsonException();

            // Normalize before constructing DateTimeOffset, whose offset is limited to ±14 hours.
            var minutes = isZulu ? 0 : int.Parse(raw.AsSpan(suffix + 1, 2), CultureInfo.InvariantCulture) * 60
                + int.Parse(raw.AsSpan(suffix + 4, 2), CultureInfo.InvariantCulture);
            if (!isZulu && raw[suffix] == '-')
                minutes = -minutes;
            var ticks = local.Ticks - minutes * TimeSpan.TicksPerMinute;
            if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
                throw new JsonException();
            return new DateTimeOffset(ticks, TimeSpan.Zero);
        }

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
        {
            if (value.Ticks % 10 != 0)
                throw new GenerationContractException();
            writer.WriteStringValue(value.ToUniversalTime().ToString(
                value.Ticks % TimeSpan.TicksPerSecond == 0
                    ? "yyyy-MM-dd'T'HH:mm:ss'Z'"
                    : "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
                CultureInfo.InvariantCulture));
        }
    }
}
