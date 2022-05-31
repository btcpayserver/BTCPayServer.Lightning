using System;
using System.Globalization;
using System.Reflection;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
                    // Fix for Eclair having empty objects for zero amount cases, see https://acinq.github.io/eclair/#globalbalance
                    JsonToken.StartObject => JObject.Load(reader) != null ? LightMoney.Zero : null,
                    // Eclair denominates global balance amounts in BTC, see https://acinq.github.io/eclair/#globalbalance
                    JsonToken.Float => new LightMoney(Convert.ToDecimal(reader.Value), LightMoneyUnit.BTC),
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
