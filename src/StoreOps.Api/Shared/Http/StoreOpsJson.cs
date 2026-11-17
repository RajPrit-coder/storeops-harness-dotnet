using System.Text.Json;
using System.Text.Json.Serialization;
using StoreOps.Api.Shared.Errors;

namespace StoreOps.Api.Shared.Http;

/// <summary>
/// JSON conventions. Enums travel ONLY as the spec's SCREAMING_SNAKE values (e.g. <c>IN_PROGRESS</c>,
/// <c>STORE_MANAGER</c>), exactly and case-sensitively, in bodies and query strings alike. C# member
/// names (<c>InProgress</c>), other casings and numbers are rejected with VALIDATION_FAILED.
/// </summary>
public static class StoreOpsJson
{
    public static JsonNamingPolicy EnumNaming => JsonNamingPolicy.SnakeCaseUpper;

    public static void Apply(JsonSerializerOptions options) => options.Converters.Add(new WireEnumConverterFactory());

    /// <summary>The wire name of an enum value, e.g. <c>ActivityStatus.InProgress</c> → <c>IN_PROGRESS</c>.</summary>
    public static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum
        => EnumNaming.ConvertName(value.ToString());

    /// <summary>Parses a wire value; throws <see cref="ValidationError"/> for anything else.</summary>
    public static TEnum ParseWire<TEnum>(string raw, string field)
        where TEnum : struct, Enum
        => WireValues<TEnum>.ByName.TryGetValue(raw, out var value)
            ? value
            : throw new ValidationError($"'{raw}' is not a valid {field}. Allowed values: {WireValues<TEnum>.Allowed}.", field);

    /// <summary>Parses an optional enum query parameter written in wire format.</summary>
    public static TEnum? ParseQuery<TEnum>(string? raw, string field)
        where TEnum : struct, Enum
        => string.IsNullOrEmpty(raw) ? null : ParseWire<TEnum>(raw, field);

    private static class WireValues<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly Dictionary<string, TEnum> ByName = Enum.GetValues<TEnum>().ToDictionary(v => WireName(v), StringComparer.Ordinal);

        public static readonly string Allowed = string.Join(", ", ByName.Keys);
    }

    private sealed class WireEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
            => (JsonConverter)Activator.CreateInstance(typeof(WireEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class WireEnumConverter<TEnum> : JsonConverter<TEnum>
        where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var field = JsonNamingPolicy.CamelCase.ConvertName(typeof(TEnum).Name);
            return reader.TokenType == JsonTokenType.String
                ? ParseWire<TEnum>(reader.GetString()!, field)
                : throw new ValidationError($"{field} must be one of: {WireValues<TEnum>.Allowed}.", field);
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
            => writer.WriteStringValue(WireName(value));
    }
}
