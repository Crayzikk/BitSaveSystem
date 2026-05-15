using System.Collections.Generic;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// ScriptableObject з мапою <c>prefabGuid → prefab</c>. Заповнюється
    /// автоматично при імпорті префабів через <c>PrefabRegistryPostprocessor</c>
    /// (див. Editor/), і вручну через інспектор.
    ///
    /// При завантаженні Mode 2 SceneRestore шукає префаб тут за GUID,
    /// інстансує його і застосовує дельту змінених полів.
    /// </summary>
    [CreateAssetMenu(menuName = "BitSaveSystem/Prefab Registry", fileName = "PrefabRegistry")]
    public sealed class PrefabRegistry : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            public string guid;
            public GameObject prefab;
        }

        [SerializeField] private List<Entry> _entries = new();

        private Dictionary<string, GameObject> _byGuid;
        private Dictionary<GameObject, string> _byPrefab;

        public static PrefabRegistry Instance { get; private set; }

        private void OnEnable()
        {
            Instance = this;
            BuildLookups();
        }

        private void BuildLookups()
        {
            _byGuid    = new Dictionary<string, GameObject>(_entries.Count);
            _byPrefab  = new Dictionary<GameObject, string>(_entries.Count);
            foreach (var e in _entries)
            {
                if (string.IsNullOrEmpty(e.guid) || e.prefab == null) continue;
                _byGuid[e.guid]    = e.prefab;
                _byPrefab[e.prefab] = e.guid;
            }
        }

        public GameObject FindPrefab(string guid)
        {
            if (_byGuid == null) BuildLookups();
            return _byGuid.TryGetValue(guid, out var p) ? p : null;
        }

        public string FindGuid(GameObject prefab)
        {
            if (_byPrefab == null) BuildLookups();
            return prefab != null && _byPrefab.TryGetValue(prefab, out var g) ? g : null;
        }

#if UNITY_EDITOR
        public void EditorAddOrUpdate(string guid, GameObject prefab)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].guid == guid) { _entries[i] = new Entry { guid = guid, prefab = prefab }; goto rebuild; }
            }
            _entries.Add(new Entry { guid = guid, prefab = prefab });
            rebuild:
            BuildLookups();
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
