using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MessagePack;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Читає/записує серіалізовувані поля Unity-компонентів через рефлексію.
    /// Використовується SceneCapture/SceneRestore для Mode 2.
    ///
    /// За замовчуванням пропускає UnityEngine.Object посилання (Material, Mesh, Texture),
    /// бо їх не можна серіалізувати без посилання на asset.
    ///
    /// СПЕЦІАЛЬНА ОБРОБКА:
    /// - PrimitiveTypeMarker: компонент, який автоматично додається при CreatePrimitive
    ///   спавні (через PrimitiveHelper). Зберігає тип примітиву і колір, при Restore
    ///   перестворює меш + матеріал через CreatePrimitive.
    /// </summary>
    public static class ComponentReflector
    {
        private static readonly Dictionary<Type, FieldInfo[]> _cache = new();
        private static readonly HashSet<Type> _supported = new()
        {
            typeof(bool), typeof(byte), typeof(sbyte), typeof(short), typeof(ushort),
            typeof(int),  typeof(uint), typeof(long), typeof(ulong),
            typeof(float),typeof(double), typeof(decimal),
            typeof(char), typeof(string),
            typeof(Vector2), typeof(Vector3), typeof(Vector4), typeof(Quaternion),
            typeof(Color), typeof(Color32), typeof(Rect), typeof(Bounds),
        };

        public static FieldInfo[] GetSerializableFields(Type t)
        {
            if (_cache.TryGetValue(t, out var cached)) return cached;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var list = new List<FieldInfo>();

            foreach (var f in t.GetFields(flags))
            {
                if (f.IsStatic) continue;
                if (f.IsNotSerialized) continue;
                if (f.GetCustomAttribute<SaveIgnoreAttribute>() != null) continue;

                bool isPublic = f.IsPublic;
                bool hasSerializeField = f.GetCustomAttribute<SerializeField>() != null;
                if (!isPublic && !hasSerializeField) continue;
                if (!IsSupportedType(f.FieldType)) continue;

                list.Add(f);
            }
            var arr = list.ToArray();
            _cache[t] = arr;
            return arr;
        }

        private static bool IsSupportedType(Type t)
        {
            if (t.IsEnum) return true;
            if (_supported.Contains(t)) return true;
            if (t.IsArray && IsSupportedType(t.GetElementType())) return true;
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)
                && IsSupportedType(t.GetGenericArguments()[0])) return true;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
            return t.IsValueType || t.IsClass;
        }

        public static Dictionary<string, byte[]> CaptureFields(Component c)
        {
            var fields = GetSerializableFields(c.GetType());
            var result = new Dictionary<string, byte[]>(fields.Length);
            foreach (var f in fields)
            {
                try
                {
                    object val = f.GetValue(c);
                    result[f.Name] = MessagePackSerializer.Serialize(f.FieldType, val, MessagePackOptions.Standard);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BitSaveSystem] Cannot serialize {c.GetType().Name}.{f.Name}: {e.Message}");
                }
            }
            return result;
        }

        public static void RestoreFields(Component c, Dictionary<string, byte[]> data)
        {
            if (data == null) return;
            var fields = GetSerializableFields(c.GetType());
            foreach (var f in fields)
            {
                if (!data.TryGetValue(f.Name, out var bytes)) continue;
                try
                {
                    object val = MessagePackSerializer.Deserialize(f.FieldType, bytes, MessagePackOptions.Standard);
                    f.SetValue(c, val);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BitSaveSystem] Cannot deserialize {c.GetType().Name}.{f.Name}: {e.Message}");
                }
            }
        }

        public static Dictionary<string, byte[]> CaptureDelta(Component c, Component reference)
        {
            var fields = GetSerializableFields(c.GetType());
            var result = new Dictionary<string, byte[]>();
            foreach (var f in fields)
            {
                try
                {
                    object cur  = f.GetValue(c);
                    object refV = f.GetValue(reference);
                    byte[] curBytes = MessagePackSerializer.Serialize(f.FieldType, cur, MessagePackOptions.Standard);
                    byte[] refBytes = MessagePackSerializer.Serialize(f.FieldType, refV, MessagePackOptions.Standard);
                    if (!ByteArrayEquals(curBytes, refBytes))
                        result[f.Name] = curBytes;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[BitSaveSystem] Delta failed on {c.GetType().Name}.{f.Name}: {e.Message}");
                }
            }
            return result;
        }

        private static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static Type ResolveType(string typeName)
            => Type.GetType(typeName) ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName)).FirstOrDefault(t => t != null);
    }

    // ========================================================================
    // ДОПОМІЖНИЙ КОМПОНЕНТ ДЛЯ ПРИМІТИВІВ БЕЗ ПРЕФАБА
    //
    // Якщо GameObject створено через GameObject.CreatePrimitive() — на ньому
    // є MeshFilter+MeshRenderer+Collider із посиланнями на вбудовані Unity-asset'и
    // (`Cube.fbx`, `Default-Material`, ...). Reflection-fallback не може їх
    // зберегти, бо це UnityEngine.Object посилання.
    //
    // Розв'язок: PrimitiveTypeMarker зберігає метадані (тип, колір) — ця інформація
    // серіалізується ComponentReflector нормально (примітиви + enum + Color).
    // SceneRestore при відсутності префаба перевіряє PrimitiveTypeMarker.SavedType
    // і робить CreatePrimitive(savedType) → відновлює всі рендер-компоненти.
    // ========================================================================
    [DisallowMultipleComponent]
    public sealed class PrimitiveTypeMarker : MonoBehaviour
    {
        public PrimitiveType SavedType = PrimitiveType.Cube;
        public Color SavedColor = Color.white;
    }

    /// <summary>
    /// Утиліта для створення примітивів, що підтримують Save Mode 2 без префаба.
    /// Використовуйте замість GameObject.CreatePrimitive() — додасть маркер.
    /// </summary>
    public static class PrimitiveHelper
    {
        public static GameObject CreatePrimitive(PrimitiveType type, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            go.GetComponent<Renderer>().material.color = color;
            var marker = go.AddComponent<PrimitiveTypeMarker>();
            marker.SavedType = type;
            marker.SavedColor = color;
            return go;
        }
    }
}