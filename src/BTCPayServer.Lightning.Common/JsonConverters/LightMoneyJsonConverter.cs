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

        readonly Type _longType = typeof(long).GetTypeInfo();
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType switch
                {
                    JsonToken.Null => null,
                    JsonToken.Integer => _longType.IsAssignableFrom(reader.ValueType)
                        ? new LightMoney((long)reader.Value)
                        : new LightMoney(long.MaxValue),
                    JsonToken.Float => new LightMoney(Convert.ToInt64(reader.Value)),
                    JsonToken.String =>
                        // some of the c-lightning values have a trailing "msat" that we need to remove before parsing
                        // some of the charge values have a trailing ".0" that we need to remove before parsing
                        new LightMoney(long.Parse(((string)reader.Value)
                            .Replace("msat", "")
                            .Replace(".0", ""), CultureInfo.InvariantCulture)),
                    // Fix for Eclair having empty objects for zero amount cases, see https://acinq.github.io/eclair/#globalbalance
                    JsonToken.StartObject => JObject.Load(reader) != null ? LightMoney.Zero : null,
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
