using System;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using Newtonsoft.Json;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Lightning.Eclair.JsonConverters
{
	public class EclairDateTimeJsonConverter : JsonConverter
	{
		public override bool CanConvert(Type objectType)
		{
			return typeof(DateTime).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
				typeof(DateTimeOffset).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
				typeof(DateTimeOffset?).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			if (reader.TokenType == JsonToken.Null)
				return null;
			DateTimeOffset result;
			if (reader.TokenType == JsonToken.StartObject)
				result = Utils.UnixTimeToDateTime(JObject.Load(reader)["unix"].Value<long>());
			else
				result = Utils.UnixTimeToDateTime((ulong)(long)reader.Value / 1000UL);
			if (objectType == typeof(DateTime))
				return result.UtcDateTime;
			return result;
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
			writer.WriteValue(Utils.DateTimeToUnixTime(time) * 1000UL);
		}
	}
}

