using System;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LNDhub.JsonConverters
{
    public class LndHubTimeSpanJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) =>
            typeof(int).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            return reader.TokenType switch
            {
                JsonToken.Integer => TimeSpan.FromSeconds((long)reader.Value),
                JsonToken.String => TimeSpan.FromSeconds(long.Parse((string)reader.Value)),
                _ => null
            };
        }
    }
}
