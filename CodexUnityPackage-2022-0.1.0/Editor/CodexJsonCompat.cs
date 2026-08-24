// Unity 2022 does not ship System.Text.Json.  This small compatibility surface
// keeps the plugin's JSON-RPC code identical while using Unity's supported
// com.unity.nuget.newtonsoft-json dependency underneath.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace System.Text.Json
{
    public enum JsonValueKind { Undefined, Object, Array, String, Number, True, False, Null }

    public sealed class JsonDocument : IDisposable
    {
        private readonly JToken root;
        private JsonDocument(JToken value) { root = value; }
        public JsonElement RootElement { get { return new JsonElement(root); } }
        public static JsonDocument Parse(string json) { return new JsonDocument(JToken.Parse(json)); }
        public void Dispose() { }
    }

    public struct JsonProperty
    {
        internal JsonProperty(string name, JToken value) { Name = name; Value = new JsonElement(value); }
        public string Name { get; private set; }
        public JsonElement Value { get; private set; }
    }

    public struct JsonElement
    {
        private readonly JToken token;
        internal JsonElement(JToken value) { token = value; }
        internal JToken Token { get { return token; } }
        public JsonElement this[int index]
        {
            get
            {
                var array = token as JArray;
                if (array == null) throw new InvalidOperationException("JSON value is not an array.");
                return new JsonElement(array[index]);
            }
        }
        public JsonValueKind ValueKind
        {
            get
            {
                if (token == null) return JsonValueKind.Undefined;
                switch (token.Type)
                {
                    case JTokenType.Object: return JsonValueKind.Object;
                    case JTokenType.Array: return JsonValueKind.Array;
                    case JTokenType.String: return JsonValueKind.String;
                    case JTokenType.Integer:
                    case JTokenType.Float: return JsonValueKind.Number;
                    case JTokenType.Boolean: return token.Value<bool>() ? JsonValueKind.True : JsonValueKind.False;
                    case JTokenType.Null: return JsonValueKind.Null;
                    default: return JsonValueKind.Undefined;
                }
            }
        }
        public bool TryGetProperty(string name, out JsonElement value)
        {
            var objectToken = token as JObject;
            JToken found;
            if (objectToken != null && objectToken.TryGetValue(name, out found)) { value = new JsonElement(found); return true; }
            value = default(JsonElement); return false;
        }
        public JsonElement GetProperty(string name)
        {
            JsonElement value;
            if (!TryGetProperty(name, out value)) throw new KeyNotFoundException("JSON property not found: " + name);
            return value;
        }
        public IEnumerable<JsonElement> EnumerateArray()
        {
            var array = token as JArray;
            if (array == null) yield break;
            foreach (var item in array) yield return new JsonElement(item);
        }
        public IEnumerable<JsonProperty> EnumerateObject()
        {
            var objectToken = token as JObject;
            if (objectToken == null) yield break;
            foreach (var item in objectToken.Properties()) yield return new JsonProperty(item.Name, item.Value);
        }
        public int GetArrayLength() { var array = token as JArray; return array == null ? 0 : array.Count; }
        public string GetString() { return token == null || token.Type == JTokenType.Null ? null : token.Value<string>(); }
        public bool GetBoolean() { return token != null && token.Value<bool>(); }
        public int GetInt32() { return token.Value<int>(); }
        public float GetSingle() { return token.Value<float>(); }
        public bool TryGetInt32(out int value)
        {
            try { value = token.Value<int>(); return true; }
            catch { value = 0; return false; }
        }
        public bool TryGetSingle(out float value)
        {
            try { value = token.Value<float>(); return true; }
            catch { value = 0f; return false; }
        }
        public string GetRawText() { return token == null ? string.Empty : token.ToString(Formatting.None); }
        public JsonElement Clone() { return new JsonElement(token == null ? null : token.DeepClone()); }
        public void WriteTo(Utf8JsonWriter writer) { if (writer == null) throw new ArgumentNullException("writer"); writer.WriteToken(token); }
        public override string ToString() { return token == null ? string.Empty : token.ToString(); }
    }

    public sealed class Utf8JsonWriter : IDisposable
    {
        private readonly Stream stream;
        private readonly Stack<JContainer> containers = new Stack<JContainer>();
        private JToken root;
        private string pendingProperty;
        public Utf8JsonWriter(Stream output) { stream = output ?? throw new ArgumentNullException("output"); }
        public void WriteStartObject() { var value = new JObject(); Add(value); containers.Push(value); Flush(); }
        public void WriteEndObject() { if (containers.Count == 0 || !(containers.Peek() is JObject)) throw new InvalidOperationException("JSON object stack mismatch."); containers.Pop(); Flush(); }
        public void WriteStartArray() { var value = new JArray(); Add(value); containers.Push(value); Flush(); }
        public void WriteEndArray() { if (containers.Count == 0 || !(containers.Peek() is JArray)) throw new InvalidOperationException("JSON array stack mismatch."); containers.Pop(); Flush(); }
        public void WritePropertyName(string name) { pendingProperty = name; }
        public void WriteString(string name, string value) { WritePropertyName(name); Add(value == null ? JValue.CreateNull() : new JValue(value)); }
        public void WriteBoolean(string name, bool value) { WritePropertyName(name); Add(new JValue(value)); }
        public void WriteNull(string name) { WritePropertyName(name); Add(JValue.CreateNull()); }
        internal void WriteToken(JToken value) { Add(value == null ? JValue.CreateNull() : value.DeepClone()); }
        private void Add(JToken value)
        {
            if (containers.Count == 0)
            {
                root = value;
            }
            else
            {
                var parent = containers.Peek();
                var objectParent = parent as JObject;
                if (objectParent != null)
                {
                    if (string.IsNullOrEmpty(pendingProperty)) throw new InvalidOperationException("JSON object property name is missing.");
                    objectParent[pendingProperty] = value;
                    pendingProperty = null;
                }
                else ((JArray)parent).Add(value);
            }
            Flush();
        }
        private void Flush()
        {
            if (root == null || !stream.CanWrite) return;
            var bytes = Encoding.UTF8.GetBytes(root.ToString(Formatting.None));
            stream.SetLength(0); stream.Position = 0; stream.Write(bytes, 0, bytes.Length); stream.Position = 0;
        }
        public void Dispose() { Flush(); }
    }
}
