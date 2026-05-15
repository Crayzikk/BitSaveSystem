using System;
using System.Collections.Generic;
using MessagePack;

namespace BitSaveSystem
{
    /// <summary>
    /// Кореневий об'єкт файлу. Серіалізується цілком.
    /// Mode 1 (часткове) → Tables заповнено, Scene = null.
    /// Mode 2 (повна сцена) → Scene заповнено, Tables можуть бути для гібриду.
    /// </summary>
    [MessagePackObject]
    public sealed class SaveFile
    {
        [Key(0)] public int    Version    { get; set; } = 1;
        [Key(1)] public long   TimestampUtc { get; set; }
        [Key(2)] public string SceneName  { get; set; }
        [Key(3)] public string SlotId     { get; set; }
        [Key(4)] public SaveMode Mode     { get; set; }

        /// <summary>Mode 1: таблиці помічених ISaveable.</summary>
        [Key(5)] public Dictionary<string, SaveTable> Tables { get; set; } = new();

        /// <summary>Mode 2: повний знімок сцени.</summary>
        [Key(6)] public SceneSnapshot Scene { get; set; }

        /// <summary>Хеші стану по SaveId — для пропуску незмінених на наступному save.</summary>
        [Key(7)] public Dictionary<string, ulong> Hashes { get; set; } = new();
    }

    public enum SaveMode : byte
    {
        State = 0,      // Mode 1: тільки помічені ISaveable
        FullScene = 1,  // Mode 2: вся сцена + стан
    }

    /// <summary>Метадані для UI без розшифровки основного файлу.</summary>
    [MessagePackObject]
    public sealed class SlotMeta
    {
        [Key(0)] public string SlotId       { get; set; }
        [Key(1)] public long   TimestampUtc { get; set; }
        [Key(2)] public string SceneName    { get; set; }
        [Key(3)] public int    Version      { get; set; }
        [Key(4)] public SaveMode Mode       { get; set; }
        [Key(5)] public string ScreenshotName { get; set; }
        [Key(6)] public long   FileSizeBytes { get; set; }
        [Key(7)] public bool   Encrypted    { get; set; }
        [Key(8)] public bool   Compressed   { get; set; }
    }
}
