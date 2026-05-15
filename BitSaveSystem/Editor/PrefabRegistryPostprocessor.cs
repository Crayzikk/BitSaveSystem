using System.Linq;
using BitSaveSystem;
using UnityEditor;
using UnityEngine;

namespace BitSaveSystem.EditorTools
{
    /// <summary>
    /// При імпорті будь-якого .prefab — додає його у PrefabRegistry,
    /// якщо такий є в проекті. GUID береться з AssetDatabase.
    /// Знаходить ПЕРШИЙ asset типу PrefabRegistry — або нічого не робить, якщо нема.
    /// </summary>
    public sealed class PrefabRegistryPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (imported == null || imported.Length == 0) return;

            var registry = FindRegistry();
            if (registry == null) return;

            bool any = false;
            foreach (var path in imported)
            {
                if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase)) continue;
                string guid = AssetDatabase.AssetPathToGUID(path);
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || string.IsNullOrEmpty(guid)) continue;
                registry.EditorAddOrUpdate(guid, go);
                any = true;
            }
            if (any) AssetDatabase.SaveAssets();
        }

        private static PrefabRegistry FindRegistry()
        {
            var guids = AssetDatabase.FindAssets("t:PrefabRegistry");
            if (guids.Length == 0) return null;
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<PrefabRegistry>(path);
        }
    }
}
