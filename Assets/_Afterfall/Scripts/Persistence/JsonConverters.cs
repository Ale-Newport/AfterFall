using System;
using Afterfall.Core;
using Afterfall.Domain.Items;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Afterfall.Persistence
{
    /// <summary>Writes vectors compactly as [x, y, z] (and avoids serialising computed properties).</summary>
    public sealed class Vec3Converter : JsonConverter<Vec3>
    {
        public override void WriteJson(JsonWriter writer, Vec3 value, JsonSerializer serializer)
        {
            writer.WriteStartArray();
            writer.WriteValue(value.X);
            writer.WriteValue(value.Y);
            writer.WriteValue(value.Z);
            writer.WriteEndArray();
        }

        public override Vec3 ReadJson(JsonReader reader, Type objectType, Vec3 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JToken t = JToken.Load(reader);
            if (t.Type == JTokenType.Array)
            {
                var a = (JArray)t;
                return new Vec3(a[0].Value<float>(), a[1].Value<float>(), a[2].Value<float>());
            }
            if (t.Type == JTokenType.Object)
                return new Vec3(t["X"]?.Value<float>() ?? 0f, t["Y"]?.Value<float>() ?? 0f, t["Z"]?.Value<float>() ?? 0f);
            return Vec3.Zero;
        }
    }

    public sealed class EntityIdConverter : JsonConverter<EntityId>
    {
        public override void WriteJson(JsonWriter writer, EntityId value, JsonSerializer serializer) => writer.WriteValue(value.Value);

        public override EntityId ReadJson(JsonReader reader, Type objectType, EntityId existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JToken t = JToken.Load(reader);
            return t.Type == JTokenType.Integer ? new EntityId(t.Value<long>()) : EntityId.None;
        }
    }

    /// <summary>Accepts "wood:3" (compact content form) or {"ItemId":"wood","Quantity":3}; writes the compact form.</summary>
    public sealed class ItemAmountConverter : JsonConverter<ItemAmount>
    {
        public override void WriteJson(JsonWriter writer, ItemAmount value, JsonSerializer serializer) => writer.WriteValue($"{value.ItemId}:{value.Quantity}");

        public override ItemAmount ReadJson(JsonReader reader, Type objectType, ItemAmount existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JToken t = JToken.Load(reader);
            if (t.Type == JTokenType.String)
            {
                string s = t.Value<string>();
                int colon = s.LastIndexOf(':');
                if (colon <= 0) return new ItemAmount(s.Trim(), 1);
                if (!int.TryParse(s.Substring(colon + 1), out int q)) throw new JsonSerializationException($"Invalid item amount '{s}'.");
                return new ItemAmount(s.Substring(0, colon).Trim(), q);
            }
            if (t.Type == JTokenType.Object)
                return new ItemAmount((string)(t["ItemId"] ?? t["itemId"] ?? t["id"]), (int?)(t["Quantity"] ?? t["quantity"] ?? t["qty"]) ?? 1);
            throw new JsonSerializationException("Invalid item amount.");
        }
    }

    public static class JsonDefaults
    {
        public static JsonSerializerSettings Create(bool strictMembers)
        {
            var s = new JsonSerializerSettings
            {
                MissingMemberHandling = strictMembers ? MissingMemberHandling.Error : MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                ObjectCreationHandling = ObjectCreationHandling.Replace,
                FloatParseHandling = FloatParseHandling.Double,
                Formatting = Formatting.None
            };
            s.Converters.Add(new StringEnumConverter());
            s.Converters.Add(new Vec3Converter());
            s.Converters.Add(new EntityIdConverter());
            s.Converters.Add(new ItemAmountConverter());
            return s;
        }
    }
}
