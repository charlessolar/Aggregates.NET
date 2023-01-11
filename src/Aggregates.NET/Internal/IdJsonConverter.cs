using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    public class IdJsonConverter : JsonConverter<Id>
    {
        public override Id Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {

            if (reader.TokenType == JsonTokenType.Number)
                return new Id((long)reader.GetInt64());

            var str = reader.GetString();
            Guid guid;
            return Guid.TryParse(str, out guid) ? new Id(guid) : new Id(str);
        }

        public override void Write(
            Utf8JsonWriter writer,
            Id value,
            JsonSerializerOptions options)
        {
            if (value.IsLong())
                writer.WriteNumberValue(value);
            else
                writer.WriteStringValue(value.ToString());
        }
    }
}
