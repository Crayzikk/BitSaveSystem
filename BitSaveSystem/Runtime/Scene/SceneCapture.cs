using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BitSaveSystem
{
    /// <summary>
    /// Mode 2: збирає повний знімок сцени.
    ///
    /// Кожен GameObject обробляється за пріоритетом:
    ///   1. Якщо є префаб у PrefabRegistry → пишемо prefabGuid + дельту полів (компактно, безпечно).
    ///   2. Інакше, якщо є PrimitiveTypeMarker → рефлексивний fallback (Restore викличе CreatePrimitive).
    ///   3. Інакше → рефлексивний fallback БЕЗ гарантії, що меш/матеріал відновляться.
    ///      У цьому випадку видається WARNING у Console з підказкою.
    ///
    /// Об'єкти без рендер-компонентів (порожні GameObject, контролери, тригери) — це нормально,
    /// для них попередження не потрібне.
    /// </summary>
    public static class SceneCapture
    {
        public static SceneSnapshot Capture(PrefabRegistry prefabRegistry)
        {
            var scene = SceneManager.GetActiveScene();
            var snapshot = new SceneSnapshot { SceneName = scene.name };

            var roots = scene.GetRootGameObjects();
            foreach (var root in roots)
                CaptureRecursive(root, prefabRegistry, snapshot.Objects, parentGuid: null);

            return snapshot;
        }

        private static void CaptureRecursive(
            GameObject go, PrefabRegistry registry, List<SceneObject> output, string parentGuid)
        {
            var idComp = go.GetComponent<SceneObjectId>() ?? go.AddComponent<SceneObjectId>();

            var so = new SceneObject
            {
                ObjectGuid = idComp.Guid,
                Name       = go.name,
                ActiveSelf = go.activeSelf,
                ParentGuid = parentGuid,
                Layer      = go.layer,
                Tag        = go.tag,
                LocalPosition = ToArr(go.transform.localPosition),
                LocalRotation = ToArr(go.transform.localRotation),
                LocalScale    = ToArr(go.transform.localScale),
            };

            // ---------- ISaveable таблиці ----------
            var saveables = go.GetComponents<ISaveable>();
            if (saveables.Length > 0)
            {
                so.SaveableTables = new List<SaveTable>(saveables.Length);
                foreach (var s in saveables) so.SaveableTables.Add(s.OnSave());
            }

            // ---------- стратегія збереження ----------
            string prefabGuid = TryGetPrefabGuid(go, registry, out var prefabRoot);

            if (prefabGuid != null && prefabRoot != null)
            {
                // Випадок 1: префаб у реєстрі — компактно і безпечно
                so.PrefabGuid = prefabGuid;
                so.ComponentDeltas = CaptureDeltas(go, prefabRoot);
            }
            else
            {
                // Випадок 2 і 3: fallback на повну рефлексію
                so.Components = CaptureAllComponents(go);

                // Якщо обджект має рендерінг, але немає ні префаба, ні PrimitiveTypeMarker —
                // попередимо користувача, що після Load він буде "голим".
                WarnIfNotRestorable(go);
            }

            output.Add(so);

            for (int i = 0; i < go.transform.childCount; i++)
                CaptureRecursive(go.transform.GetChild(i).gameObject, registry, output, so.ObjectGuid);
        }

        /// <summary>
        /// Якщо обджект має MeshRenderer/SkinnedMeshRenderer, але не є інстансом префаба
        /// і не має PrimitiveTypeMarker — це означає, що рефлексія не зможе відновити меш/матеріал
        /// при Load (бо це UnityEngine.Object посилання). Видаємо warning, але НЕ помилку —
        /// деякі юзкейси (тригери, контролери) свідомо не мають рендеру.
        /// </summary>
        private static void WarnIfNotRestorable(GameObject go)
        {
            var hasRenderer = go.GetComponent<Renderer>() != null;
            if (!hasRenderer) return;

            var hasMarker = go.GetComponent<PrimitiveTypeMarker>() != null;
            if (hasMarker) return;

            var hasPrefabMarker = go.GetComponent<PrefabSourceMarker>() != null;
            if (hasPrefabMarker) return;

            Debug.LogWarning(
                $"[BitSaveSystem] '{go.name}' has a Renderer but no prefab source. " +
                $"After Load Mode 2 it will be restored WITHOUT mesh/material.\n" +
                $"FIX: Right-click on object in Hierarchy → BitSaveSystem → Convert to Saveable Prefab.\n" +
                $"Or use PrimitiveHelper.CreatePrimitive() if it's a runtime primitive.",
                go);
        }

        private static string TryGetPrefabGuid(GameObject go, PrefabRegistry registry, out GameObject prefabRoot)
        {
            prefabRoot = null;
            if (registry == null) return null;
#if UNITY_EDITOR
            var src = UnityEditor.PrefabUtility.GetCorrespondingObjectFromOriginalSource(go);
            if (src != null)
            {
                prefabRoot = src;
                return registry.FindGuid(src);
            }
#endif
            var marker = go.GetComponent<PrefabSourceMarker>();
            if (marker != null && !string.IsNullOrEmpty(marker.PrefabGuid))
            {
                prefabRoot = registry.FindPrefab(marker.PrefabGuid);
                return prefabRoot != null ? marker.PrefabGuid : null;
            }
            return null;
        }

        private static List<ComponentDelta> CaptureDeltas(GameObject instance, GameObject prefab)
        {
            var deltas = new List<ComponentDelta>();
            var instComps = instance.GetComponents<Component>();
            var prefComps = prefab.GetComponents<Component>();

            var prefByType = new Dictionary<System.Type, List<Component>>();
            foreach (var c in prefComps)
            {
                if (c == null) continue;
                if (!prefByType.TryGetValue(c.GetType(), out var list)) prefByType[c.GetType()] = list = new();
                list.Add(c);
            }

            var instCounters = new Dictionary<System.Type, int>();
            foreach (var c in instComps)
            {
                if (c == null) continue;
                var t = c.GetType();
                int idx = instCounters.TryGetValue(t, out var ci) ? ci : 0;
                instCounters[t] = idx + 1;

                if (!prefByType.TryGetValue(t, out var refList) || idx >= refList.Count)
                {
                    var snap = ComponentReflector.CaptureFields(c);
                    deltas.Add(new ComponentDelta { TypeName = t.AssemblyQualifiedName, Index = idx, ChangedFields = snap });
                    continue;
                }

                var changed = ComponentReflector.CaptureDelta(c, refList[idx]);
                if (changed.Count > 0)
                    deltas.Add(new ComponentDelta { TypeName = t.AssemblyQualifiedName, Index = idx, ChangedFields = changed });
            }
            return deltas;
        }

        private static List<ComponentSnapshot> CaptureAllComponents(GameObject go)
        {
            var result = new List<ComponentSnapshot>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                if (c is Transform) continue;
                result.Add(new ComponentSnapshot
                {
                    TypeName = c.GetType().AssemblyQualifiedName,
                    Fields   = ComponentReflector.CaptureFields(c),
                });
            }
            return result;
        }

        private static float[] ToArr(Vector3 v)    => new[] { v.x, v.y, v.z };
        private static float[] ToArr(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
    }

    /// <summary>
    /// Маркер для білдів: зберігає на префабі його GUID із PrefabRegistry,
    /// щоб у білді (де UnityEditor.PrefabUtility недоступний) можна було визначити
    /// джерело-префаб об'єкта. Автоматично додається через інструмент
    /// "Convert to Saveable Prefab" або через PrefabRegistryPostprocessor.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrefabSourceMarker : MonoBehaviour
    {
        public string PrefabGuid;
    }
}