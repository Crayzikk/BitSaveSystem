using System.IO;
using BitSaveSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BitSaveSystem.EditorTools
{
    /// <summary>
    /// Інструмент конвертації звичайного сценного GameObject у saveable-префаб.
    ///
    /// Що робить:
    ///   1. Гарантує наявність PrefabRegistry у проекті (створює якщо нема).
    ///   2. Створює папку Assets/SaveablePrefabs/ якщо нема.
    ///   3. Зберігає поточний GameObject як префаб у цій папці.
    ///   4. Замінює інстанс у сцені на інстанс із новоствореного префаба
    ///      (зберігаючи позицію, обертання, активність).
    ///   5. Додає на префаб PrefabSourceMarker з GUID для роботи у білді.
    ///   6. Прописує префаб у PrefabRegistry.
    ///
    /// Доступ:
    ///   - Hierarchy → правий клік на об'єкті → "BitSaveSystem/Convert to Saveable Prefab"
    ///   - Меню "GameObject/BitSaveSystem/Convert to Saveable Prefab" (зі скороченням Ctrl+Alt+P)
    ///
    /// Працює тільки в Editor, на одному об'єкті за раз.
    /// </summary>
    public static class SaveablePrefabConverter
    {
        private const string SaveablePrefabsDir = "Assets/SaveablePrefabs";
        private const string PrefabRegistryDefaultPath = "Assets/SaveablePrefabs/PrefabRegistry.asset";

        // Меню в Hierarchy: правий клік на GameObject
        [MenuItem("GameObject/BitSaveSystem/Convert to Saveable Prefab", false, 30)]
        [MenuItem("CONTEXT/Transform/BitSaveSystem ▸ Convert to Saveable Prefab")]
        private static void ConvertSelectedFromMenu(MenuCommand cmd)
        {
            var go = cmd.context as GameObject;
            if (go == null) go = Selection.activeGameObject;
            if (go == null) return;
            Convert(go);
        }

        // Перевірка, чи доступне меню (тільки для GameObject у сцені, не для префабів)
        [MenuItem("GameObject/BitSaveSystem/Convert to Saveable Prefab", true)]
        private static bool ValidateConvert()
        {
            var go = Selection.activeGameObject;
            if (go == null) return false;
            // Не дозволяємо запускати на префабі-asset або на префабі в Prefab Mode
            if (PrefabUtility.IsPartOfPrefabAsset(go)) return false;
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) return false;
            return true;
        }

        [System.Obsolete]
        public static GameObject Convert(GameObject sceneInstance)
        {
            if (sceneInstance == null) return null;

            // Якщо це вже інстанс префаба — нічого не робимо, тільки попереджаємо
            if (PrefabUtility.GetCorrespondingObjectFromOriginalSource(sceneInstance) != null)
            {
                EditorUtility.DisplayDialog(
                    "Already a prefab",
                    $"'{sceneInstance.name}' is already an instance of a prefab. " +
                    "Make sure the prefab is registered in PrefabRegistry " +
                    "(prefabs are auto-registered when imported).",
                    "OK");
                return null;
            }

            // 1. Папка SaveablePrefabs
            if (!AssetDatabase.IsValidFolder(SaveablePrefabsDir))
            {
                Directory.CreateDirectory(SaveablePrefabsDir);
                AssetDatabase.Refresh();
            }

            // 2. Унікальний шлях до префаба
            string safeName = MakeSafeFileName(sceneInstance.name);
            string prefabPath = AssetDatabase.GenerateUniqueAssetPath(
                Path.Combine(SaveablePrefabsDir, safeName + ".prefab"));

            // 3. Зберігаємо позицію/обертання, бо після конвертації об'єкт буде заново створений
            var pos = sceneInstance.transform.position;
            var rot = sceneInstance.transform.rotation;
            var scl = sceneInstance.transform.localScale;
            var parent = sceneInstance.transform.parent;
            bool wasActive = sceneInstance.activeSelf;

            // 4. Створюємо префаб і отримуємо інстанс, прив'язаний до нього
            //    SaveAsPrefabAssetAndConnect — створює asset і одночасно перетворює scene-instance
            //    на prefab-instance, зберігаючи всі компоненти і дані.
            GameObject prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(
                sceneInstance, prefabPath, InteractionMode.UserAction);

            if (prefabAsset == null)
            {
                Debug.LogError($"[BitSaveSystem] Failed to create prefab at {prefabPath}");
                return null;
            }

            // 5. Додаємо PrefabSourceMarker на ASSET префаба (не на інстанс)
            //    щоб у білді (без UnityEditor) можна було визначити джерело-префаб.
            string prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath);
            var marker = prefabAsset.GetComponent<PrefabSourceMarker>();
            if (marker == null)
            {
                // Відкриваємо asset для редагування через PrefabUtility
                using (var editScope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
                {
                    var root = editScope.prefabContentsRoot;
                    if (root.GetComponent<PrefabSourceMarker>() == null)
                    {
                        var m = root.AddComponent<PrefabSourceMarker>();
                        m.PrefabGuid = prefabGuid;
                    }
                    else
                    {
                        root.GetComponent<PrefabSourceMarker>().PrefabGuid = prefabGuid;
                    }
                }
            }
            else
            {
                marker.PrefabGuid = prefabGuid;
                PrefabUtility.ApplyPrefabInstance(sceneInstance, InteractionMode.AutomatedAction);
            }

            // 6. Гарантуємо, що префаб у PrefabRegistry
            var registry = GetOrCreateRegistry();
            registry.EditorAddOrUpdate(prefabGuid, prefabAsset);
            EditorUtility.SetDirty(registry);

            // 7. Відновлюємо позицію (на випадок, якщо SaveAsPrefab її зкинув)
            sceneInstance.transform.SetParent(parent, true);
            sceneInstance.transform.position = pos;
            sceneInstance.transform.rotation = rot;
            sceneInstance.transform.localScale = scl;
            sceneInstance.SetActive(wasActive);

            AssetDatabase.SaveAssets();

            Debug.Log($"[BitSaveSystem] Converted '{sceneInstance.name}' → prefab '{prefabPath}' " +
                      $"(guid: {prefabGuid[..8]}...) and registered in PrefabRegistry.");

            // Фокус на створений asset
            EditorGUIUtility.PingObject(prefabAsset);
            return prefabAsset;
        }

        [System.Obsolete]
        private static PrefabRegistry GetOrCreateRegistry()
        {
            // Спершу шукаємо існуючий у проекті
            var guids = AssetDatabase.FindAssets("t:PrefabRegistry");
            if (guids.Length > 0)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                var existing = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(path);
                if (existing != null) return existing;
            }

            // Створюємо новий
            if (!AssetDatabase.IsValidFolder(SaveablePrefabsDir))
            {
                Directory.CreateDirectory(SaveablePrefabsDir);
                AssetDatabase.Refresh();
            }

            var asset = ScriptableObject.CreateInstance<PrefabRegistry>();
            AssetDatabase.CreateAsset(asset, PrefabRegistryDefaultPath);
            AssetDatabase.SaveAssets();

            // Прив'язуємо до SaveManager у поточній сцені, якщо є і поле порожнє
            var sm = Object.FindObjectOfType<SaveManager>();
            if (sm != null)
            {
                var so = new SerializedObject(sm);
                var prop = so.FindProperty("_prefabRegistry");
                if (prop != null && prop.objectReferenceValue == null)
                {
                    prop.objectReferenceValue = asset;
                    so.ApplyModifiedProperties();
                    Debug.Log("[BitSaveSystem] PrefabRegistry created and bound to SaveManager.");
                }
            }

            return asset;
        }

        private static string MakeSafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
