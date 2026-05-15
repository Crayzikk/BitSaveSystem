using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BitSaveSystem
{
    /// <summary>
    /// Mode 2 завантаження.
    /// Стратегія:
    ///   1. Збираємо мапу існуючих GO у сцені за SceneObjectId.Guid.
    ///   2. Для кожного об'єкта в snapshot:
    ///      - Якщо існує в сцені (за GUID) — оновлюємо стан.
    ///      - Якщо нема, але є prefabGuid — Instantiate(prefab) → застосовуємо дельту.
    ///      - Якщо нема ні в сцені, ні префаба — створюємо GO:
    ///        * Якщо серед збережених компонентів є PrimitiveTypeMarker — викликаємо
    ///          GameObject.CreatePrimitive(SavedType) → отримуємо меш/матеріал/колайдер.
    ///        * Інакше — порожній GameObject + AddComponent кожного типу через рефлексію.
    ///   3. Видаляємо з сцени GO, яких нема в snapshot (опційно).
    ///   4. Двопрохідно встановлюємо parent.
    ///   5. Відновлюємо ISaveable.OnLoad для кожного GO.
    /// </summary>
    public static class SceneRestore
    {
        public static void Apply(SceneSnapshot snapshot, PrefabRegistry registry, bool destroyMissing = true)
        {
            if (snapshot == null) return;

            var existing = new Dictionary<string, GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                CollectExisting(root, existing);

            var seen = new HashSet<string>();
            var created = new Dictionary<string, GameObject>();

            // Прохід 1: створення / оновлення GO
            foreach (var so in snapshot.Objects)
            {
                seen.Add(so.ObjectGuid);
                GameObject go;

                if (existing.TryGetValue(so.ObjectGuid, out go))
                {
                    // Об'єкт вже у сцені — просто оновимо нижче
                }
                else if (!string.IsNullOrEmpty(so.PrefabGuid) && registry != null)
                {
                    var prefab = registry.FindPrefab(so.PrefabGuid);
                    if (prefab != null)
                    {
                        go = Object.Instantiate(prefab);
                        var idComp = go.GetComponent<SceneObjectId>() ?? go.AddComponent<SceneObjectId>();
                        SetGuid(idComp, so.ObjectGuid);
                    }
                    else
                    {
                        Debug.LogWarning($"[BitSaveSystem] Prefab '{so.PrefabGuid}' not found in registry. Skipping {so.Name}.");
                        continue;
                    }
                }
                else
                {
                    // Без префаба — створюємо вручну.
                    // СПОЧАТКУ перевіряємо, чи серед компонентів є PrimitiveTypeMarker —
                    // тоді треба викликати GameObject.CreatePrimitive() щоб мати меш+матеріал.
                    var primitiveType = FindPrimitiveType(so, out var primitiveColor);
                    if (primitiveType.HasValue)
                    {
                        go = PrimitiveHelper.CreatePrimitive(primitiveType.Value, primitiveColor);
                        go.name = so.Name;
                    }
                    else
                    {
                        go = new GameObject(so.Name);
                    }

                    var idComp = go.GetComponent<SceneObjectId>() ?? go.AddComponent<SceneObjectId>();
                    SetGuid(idComp, so.ObjectGuid);

                    if (so.Components != null)
                    {
                        foreach (var cs in so.Components)
                        {
                            var t = ComponentReflector.ResolveType(cs.TypeName);
                            if (t == null || !typeof(Component).IsAssignableFrom(t)) continue;

                            // Transform, MeshFilter, MeshRenderer, BoxCollider, SphereCollider тощо
                            // вже додані через CreatePrimitive — на них тільки відновлюємо поля.
                            // PrimitiveTypeMarker теж уже доданий PrimitiveHelper.CreatePrimitive.
                            var c = go.GetComponent(t);
                            if (c == null)
                            {
                                try { c = go.AddComponent(t); }
                                catch (System.Exception e)
                                {
                                    Debug.LogWarning($"[BitSaveSystem] Cannot AddComponent {t.Name} on '{so.Name}': {e.Message}");
                                    continue;
                                }
                            }
                            if (c != null) ComponentReflector.RestoreFields(c, cs.Fields);
                        }
                    }
                }

                if (go == null) continue;

                // Базові властивості
                go.name = so.Name;
                go.SetActive(so.ActiveSelf);
                go.layer = so.Layer;
                if (!string.IsNullOrEmpty(so.Tag)) try { go.tag = so.Tag; } catch { /* unknown tag */ }

                if (so.LocalPosition is { Length: 3 })
                    go.transform.localPosition = new Vector3(so.LocalPosition[0], so.LocalPosition[1], so.LocalPosition[2]);
                if (so.LocalRotation is { Length: 4 })
                    go.transform.localRotation = new Quaternion(so.LocalRotation[0], so.LocalRotation[1], so.LocalRotation[2], so.LocalRotation[3]);
                if (so.LocalScale is { Length: 3 })
                    go.transform.localScale = new Vector3(so.LocalScale[0], so.LocalScale[1], so.LocalScale[2]);

                if (so.ComponentDeltas != null)
                    ApplyDeltas(go, so.ComponentDeltas);

                if (so.SaveableTables != null)
                {
                    var saveables = go.GetComponents<ISaveable>();
                    int n = Mathf.Min(saveables.Length, so.SaveableTables.Count);
                    for (int i = 0; i < n; i++) saveables[i].OnLoad(so.SaveableTables[i]);
                }

                created[so.ObjectGuid] = go;
            }

            // Прохід 2: parent-child
            foreach (var so in snapshot.Objects)
            {
                if (!created.TryGetValue(so.ObjectGuid, out var go))
                    existing.TryGetValue(so.ObjectGuid, out go);
                if (go == null) continue;

                if (string.IsNullOrEmpty(so.ParentGuid))
                {
                    go.transform.SetParent(null, worldPositionStays: false);
                }
                else
                {
                    GameObject parent = null;
                    if (!created.TryGetValue(so.ParentGuid, out parent))
                        existing.TryGetValue(so.ParentGuid, out parent);
                    if (parent != null) go.transform.SetParent(parent.transform, worldPositionStays: false);
                }
            }

            // Прохід 3: видалити те, чого нема в snapshot
            if (destroyMissing)
            {
                foreach (var kv in existing)
                {
                    if (!seen.Contains(kv.Key)) Object.Destroy(kv.Value);
                }
            }
        }

        /// <summary>
        /// Шукає серед збережених компонентів PrimitiveTypeMarker. Якщо знайдено —
        /// повертає PrimitiveType, щоб SceneRestore викликав CreatePrimitive,
        /// який відразу додасть меш, матеріал, колайдер.
        /// </summary>
        private static PrimitiveType? FindPrimitiveType(SceneObject so, out Color color)
        {
            color = Color.white;
            if (so.Components == null) return null;

            foreach (var cs in so.Components)
            {
                var t = ComponentReflector.ResolveType(cs.TypeName);
                if (t != typeof(PrimitiveTypeMarker)) continue;

                if (cs.Fields.TryGetValue("SavedType", out var typeBytes))
                {
                    int typeInt = MessagePack.MessagePackSerializer.Deserialize<int>(typeBytes, MessagePackOptions.Standard);
                    if (cs.Fields.TryGetValue("SavedColor", out var colBytes))
                        color = MessagePack.MessagePackSerializer.Deserialize<Color>(colBytes, MessagePackOptions.Standard);
                    return (PrimitiveType)typeInt;
                }
                return PrimitiveType.Cube;
            }
            return null;
        }

        private static void SetGuid(SceneObjectId idComp, string guid)
        {
            var f = typeof(SceneObjectId).GetField("_guid",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            f?.SetValue(idComp, guid);
        }

        private static void CollectExisting(GameObject go, Dictionary<string, GameObject> map)
        {
            var id = go.GetComponent<SceneObjectId>();
            if (id != null) map[id.Guid] = go;
            for (int i = 0; i < go.transform.childCount; i++)
                CollectExisting(go.transform.GetChild(i).gameObject, map);
        }

        private static void ApplyDeltas(GameObject go, List<ComponentDelta> deltas)
        {
            var byTypeOnInst = new Dictionary<System.Type, List<Component>>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                var t = c.GetType();
                if (!byTypeOnInst.TryGetValue(t, out var l)) byTypeOnInst[t] = l = new();
                l.Add(c);
            }

            foreach (var d in deltas)
            {
                var t = ComponentReflector.ResolveType(d.TypeName);
                if (t == null) continue;

                Component target = null;
                if (byTypeOnInst.TryGetValue(t, out var list) && d.Index < list.Count)
                    target = list[d.Index];
                else
                    target = go.AddComponent(t);

                ComponentReflector.RestoreFields(target, d.ChangedFields);
            }
        }
    }
}