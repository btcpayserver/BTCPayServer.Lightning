using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Reflection;
using NBitcoin.JsonConverters;
using System.Globalization;

namespace BTCPayServer.Lightning.JsonConverters
{
    public class LightMoneyJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(LightMoney).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        Type longType = typeof(long).GetTypeInfo();
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType switch
                {
                    JsonToken.Null => null,
                    JsonToken.Integer => longType.IsAssignableFrom(reader.ValueType)
                        ? new LightMoney((long)reader.Value)
                        : new LightMoney(long.MaxValue),
                    JsonToken.String =>
                        // some of the c-lightning values have a trailing "msat" that we need to remove before parsing
                        new LightMoney(long.Parse(((string)reader.Value).Replace("msat", ""), CultureInfo.InvariantCulture)),
                    _ => null
                };
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Money amount should be in millisatoshi", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((LightMoney)value).MilliSatoshi);
        }
    }
}
