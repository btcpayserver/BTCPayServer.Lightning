using System;
using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LNDhub.JsonConverters
{
    public class LndHubBufferJsonConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType) =>
            typeof(string).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.StartObject:
                    var obj = JObject.Load(reader);
                    return obj["type"].Value<string>() == "Buffer" && obj["data"] != null
                        ? BitString(obj["data"].ToObject<byte[]>())
                        : null;

                default:
                    return null;
            }
        }

        private static string BitString(byte[] bytes) =>
            BitConverter.ToString(bytes)
                .Replace("-", "")
                .ToLower(CultureInfo.InvariantCulture);
    }
}
