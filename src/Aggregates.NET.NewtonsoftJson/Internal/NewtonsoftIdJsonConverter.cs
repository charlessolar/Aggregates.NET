using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Newtonsoft.Json;

namespace Aggregates.Internal
{
    [ExcludeFromCodeCoverage]
    class NewtonsoftIdJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => typeof(Id) == objectType;

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Integer)
                return new Id((long)reader.Value);

            var str = reader.Value as string;
            Guid guid;
            return Guid.TryParse(str, out guid) ? new Id(guid) : new Id(str);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var id = (Id)value;
            writer.WriteValue(id.Value);
        }
    }
}
