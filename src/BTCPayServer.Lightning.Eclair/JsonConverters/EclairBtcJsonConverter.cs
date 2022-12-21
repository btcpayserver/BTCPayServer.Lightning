using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.Eclair.JsonConverters
{
    public class EclairBtcJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(LightMoney).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        readonly Type _longType = typeof(long).GetTypeInfo();
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType switch
                {
                    JsonToken.Null => null,
                    JsonToken.Integer => _longType.IsAssignableFrom(reader.ValueType)
                        ? new LightMoney((long)reader.Value, LightMoneyUnit.BTC)
                        : new LightMoney(long.MaxValue, LightMoneyUnit.BTC),
                    // Eclair denominates global balance amounts in BTC, see https://acinq.github.io/eclair/#globalbalance
                    JsonToken.Float => new LightMoney(Convert.ToDecimal(reader.Value), LightMoneyUnit.BTC),
                    JsonToken.String =>
                        // some of the c-lightning values have a trailing "msat" that we need to remove before parsing
                        new LightMoney(long.Parse(((string)reader.Value).Replace("msat", ""), CultureInfo.InvariantCulture), LightMoneyUnit.BTC),
                    // Fix for Eclair having empty objects for zero amount cases, see https://acinq.github.io/eclair/#globalbalance
                    JsonToken.StartObject => JObject.Load(reader) != null ? LightMoney.Zero : null,
                    _ => null
                };
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Money amount should be in BTC", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((LightMoney)value).ToUnit(LightMoneyUnit.Bit));
        }
    }
}

