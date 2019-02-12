using System;
using System.Globalization;
using System.Reflection;
using BTCPayServer.Lightning.CLightning;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.JsonConverters
{
    public class ShortChannelIdJsonConverter: JsonConverter
    {
        
        public override bool CanConvert(Type objectType)
        {
            return typeof(ShortChannelIdJsonConverter).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        Type stringType = typeof(string).GetTypeInfo();
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType == JsonToken.Null ? null :
                    reader.TokenType == JsonToken.String ? new ShortChannelId((string)reader.Value)
                    : null;
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Short Channel Id must be in string", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((ShortChannelId)value).ToString());
        }
    }
}