using System;
using System.Reflection;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.JsonConverters
{
    public class LndHubDateTimeOffsetConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(DateTime).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
                   typeof(DateTimeOffset).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
                   typeof(DateTimeOffset?).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.Value == null || reader.TokenType == JsonToken.Null) return null;
                
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                {
                    return Utils.UnixTimeToDateTime((ulong)(long)reader.Value);
                }
                case JsonToken.String:
                {
                    return Utils.UnixTimeToDateTime(long.Parse((string)reader.Value));
                }
            }
            
            return null;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            DateTime time;
            if (value is DateTime)
                time = (DateTime)value;
            else
                time = ((DateTimeOffset)value).UtcDateTime;

            if (time < Utils.UnixTimeToDateTime(0))
                time = Utils.UnixTimeToDateTime(0).UtcDateTime;
            writer.WriteValue(Utils.DateTimeToUnixTime(time));
        }
    }
}
