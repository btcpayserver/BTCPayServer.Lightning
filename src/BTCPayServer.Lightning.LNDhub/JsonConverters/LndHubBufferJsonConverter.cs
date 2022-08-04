using System;
using System.Globalization;
using System.Reflection;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.LNDhub.JsonConverters
{
    public class LndHubBufferJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            typeof(string).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.StartObject) return null;
        
            var obj = JObject.Load(reader);
            return obj["type"]?.Value<string>() == "Buffer" && obj["data"] != null
                ? new uint256(BitString(obj["data"].ToObject<byte[]>()))
                : null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case uint256 val:
                    writer.WriteValue(val.ToString());
                    break;
                case string str:
                    writer.WriteValue(str);
                    break;
                default:
                    writer.WriteNull();
                    break;
            }
        }
    
        private static string BitString(byte[] bytes) =>
            BitConverter.ToString(bytes)
                .Replace("-", "")
                .ToLower(CultureInfo.InvariantCulture);
    }
}
