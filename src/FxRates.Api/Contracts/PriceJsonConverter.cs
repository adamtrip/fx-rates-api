using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FxRates.Application.Rates;

namespace FxRates.Api.Contracts;

/// <summary>Validate raw price digits before decimal parsing can round them. Applied only to request price properties.</summary>
public sealed class PriceJsonConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
        {
            throw new JsonException("Prices must be JSON numbers.");
        }
        var raw = reader.HasValueSequence
            ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
            : Encoding.UTF8.GetString(reader.ValueSpan);
        if (!PriceParser.TryParse(raw, out var value))
        {
            throw new JsonException(
                "Prices must use ordinary decimal notation with at most eight significant fractional digits.");
        }
        return value;
    }

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}
