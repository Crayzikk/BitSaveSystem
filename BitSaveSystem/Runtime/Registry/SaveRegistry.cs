using System.Collections.Generic;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Статичний реєстр усіх активних ISaveable у грі.
    /// Замінює FindObjectsOfType — O(1) додавання/видалення.
    /// </summary>
    public static class SaveRegistry
    {
        private static readonly HashSet<ISaveable> _set = new();
        private static readonly Dictionary<string, ISaveable> _byId = new();

        public static IReadOnlyCollection<ISaveable> All => _set;
        public static int Count => _set.Count;

        public static void Register(ISaveable saveable)
        {
            if (saveable == null) return;
            if (string.IsNullOrEmpty(saveable.SaveId))
            {
                Debug.LogError($"[BitSaveSystem] {saveable.GetType().Name} has empty SaveId. Skipped.");
                return;
            }

            if (_byId.TryGetValue(saveable.SaveId, out var existing) && !ReferenceEquals(existing, saveable))
            {
                Debug.LogWarning($"[BitSaveSystem] Duplicate SaveId '{saveable.SaveId}' " +
                                 $"({existing.GetType().Name} vs {saveable.GetType().Name}). Second overrides first.");
            }

            _set.Add(saveable);
            _byId[saveable.SaveId] = saveable;
        }

        public static void Unregister(ISaveable saveable)
        {
            if (saveable == null) return;
            _set.Remove(saveable);
            if (_byId.TryGetValue(saveable.SaveId, out var existing) && ReferenceEquals(existing, saveable))
                _byId.Remove(saveable.SaveId);
        }

        public static ISaveable Find(string id) => _byId.TryGetValue(id, out var s) ? s : null;

        public static void Clear()
        {
            _set.Clear();
            _byId.Clear();
        }

        /// <summary>Чи є хоча б один ISaveable з нескасованими змінами (для AutoSave).</summary>
        public static bool AnyDirty(IReadOnlyDictionary<string, ulong> previousHashes)
        {
            if (previousHashes == null || previousHashes.Count == 0) return _set.Count > 0;
            foreach (var s in _set)
            {
                if (!previousHashes.TryGetValue(s.SaveId, out var prevHash)) return true;
                if (s.ComputeStateHash() != prevHash) return true;
            }
            return false;
        }
    }
}
