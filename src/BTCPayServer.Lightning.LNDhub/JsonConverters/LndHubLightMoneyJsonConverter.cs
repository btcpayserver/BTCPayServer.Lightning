using System;
using System.Reflection;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Lightning.LNDhub.JsonConverters
{
    public class LndHubLightMoneyJsonConverter : JsonConverter
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
                        ? LightMoney.Satoshis((long)reader.Value)
                        : LightMoney.Satoshis(long.MaxValue),
                    JsonToken.String =>
                        LightMoney.Satoshis(long.Parse((string)reader.Value)),
                    _ => null
                };
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Amount should be in satoshi", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue((int)((LightMoney)value).ToUnit(LightMoneyUnit.Satoshi));
            else
                writer.WriteNull();
        }
    }
}
