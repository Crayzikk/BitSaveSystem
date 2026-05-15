using System;
using System.Collections.Generic;
using MessagePack;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Універсальна таблиця "ключ → серіалізовані байти".
    /// Кожен <see cref="ISaveable"/> має свою таблицю.
    /// </summary>
    [MessagePackObject]
    public sealed class SaveTable
    {
        [Key(0)] public string TypeKey { get; set; }
        [Key(1)] public Dictionary<string, byte[]> Fields { get; set; } = new();

        // ---------- factory ----------

        public static SaveTable From(object owner) => new() { TypeKey = owner.GetType().Name };
        public static SaveTable From(object owner, string suffix)
            => new() { TypeKey = owner.GetType().Name + "." + suffix };
        public static SaveTable From(string customKey) => new() { TypeKey = customKey };

        // ---------- write ----------

        public SaveTable Set<T>(string key, T value)
        {
            Fields[key] = MessagePackSerializer.Serialize(value, MessagePackOptions.Standard);
            return this;
        }

        public SaveTable SetVector3(string key, Vector3 v)
            => Set(key, new[] { v.x, v.y, v.z });

        public SaveTable SetQuaternion(string key, Quaternion q)
            => Set(key, new[] { q.x, q.y, q.z, q.w });

        public SaveTable SetColor(string key, Color c)
            => Set(key, new[] { c.r, c.g, c.b, c.a });

        // ---------- read ----------

        public bool Has(string key) => Fields.ContainsKey(key);

        public T Get<T>(string key, T defaultValue = default)
        {
            if (!Fields.TryGetValue(key, out var bytes)) return defaultValue;
            try
            {
                return MessagePackSerializer.Deserialize<T>(bytes, MessagePackOptions.Standard);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[BitSaveSystem] Field '{key}' deserialization failed: {e.Message}. Returning default.");
                return defaultValue;
            }
        }

        public Vector3 GetVector3(string key, Vector3 def = default)
        {
            var arr = Get<float[]>(key);
            return arr is { Length: >= 3 } ? new Vector3(arr[0], arr[1], arr[2]) : def;
        }

        public Quaternion GetQuaternion(string key, Quaternion def = default)
        {
            var arr = Get<float[]>(key);
            return arr is { Length: >= 4 } ? new Quaternion(arr[0], arr[1], arr[2], arr[3]) : def;
        }

        public Color GetColor(string key, Color def = default)
        {
            var arr = Get<float[]>(key);
            return arr is { Length: >= 4 } ? new Color(arr[0], arr[1], arr[2], arr[3]) : def;
        }
    }
}
