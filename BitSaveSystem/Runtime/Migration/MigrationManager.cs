using System.Collections.Generic;
using UnityEngine;

namespace BitSaveSystem
{
    public interface IMigrator
    {
        int FromVersion { get; }
        int ToVersion   { get; }
        SaveFile Migrate(SaveFile file);
    }

    public static class MigrationManager
    {
        private static readonly List<IMigrator> _migrators = new();

        /// <summary>Реєструється кодером з його ігрової збірки.</summary>
        public static void Register(IMigrator m) => _migrators.Add(m);

        public static void Clear() => _migrators.Clear();

        public static SaveFile Migrate(SaveFile file, int currentVersion)
        {
            while (file.Version < currentVersion)
            {
                bool found = false;
                foreach (var m in _migrators)
                {
                    if (m.FromVersion == file.Version)
                    {
                        Debug.Log($"[BitSaveSystem] Migrating v{m.FromVersion} → v{m.ToVersion}");
                        file = m.Migrate(file);
                        file.Version = m.ToVersion;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Debug.LogError($"[BitSaveSystem] No migrator from v{file.Version}. Save may be partially loaded.");
                    break;
                }
            }
            return file;
        }
    }
}
