using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using BitSaveSystem.Diagnostics;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace BitSaveSystem
{
    /// <summary>
    /// Єдина точка входу. Singleton MonoBehaviour. Тримає всі сервіси.
    ///
    /// Mode 1 (State): зберігає тільки помічені ISaveable-об'єкти.
    /// Mode 2 (FullScene): зберігає ВСЮ сцену через SceneCapture.
    ///
    /// Налаштування — у Inspector компонента або програмно через властивості.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
    public sealed class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        // ---------- options (Inspector) ----------

        [Header("Storage")]
        [Tooltip("Куди писати файли. Якщо порожнє — Application.persistentDataPath.")]
        [SerializeField] private string _customSavePath = "";
        [Tooltip("Використати папку проекту Assets/SaveData (тільки в Editor).")]
        [SerializeField] private bool _useAssetsFolderInEditor = true;

        [Header("Features (toggleable)")]
        [SerializeField] private bool _encryptionEnabled  = true;
        [SerializeField] private bool _compressionEnabled = true;
        [SerializeField] private bool _rollbackEnabled    = true;
        [SerializeField, Range(1, 20)] private int _maxSnapshots = 5;

        [Header("Versioning")]
        [SerializeField] private int _currentVersion = 1;

        [Header("Prefab Registry (for Mode 2)")]
        [SerializeField] private PrefabRegistry _prefabRegistry;

        [Header("AutoSave")]
        [SerializeField] private AutoSaveConfig _autoSaveConfig;

        // ---------- services ----------

        private SaveSerializer    _serializer;
        private EncryptionService _encryption;
        private RollbackService   _rollback;
        private AutoSaveService   _autoSave;

        private string _saveDir;

        // Кеш хешів останнього збереження по slotId — для пропуску незмінених
        private readonly Dictionary<string, Dictionary<string, ulong>> _lastHashes = new();

        // ---------- properties ----------

        public string SaveDir => _saveDir;
        public bool   EncryptionEnabled  { get => _encryptionEnabled;  set { _encryptionEnabled = value;  RebuildEncryption(); } }
        public bool   CompressionEnabled { get => _compressionEnabled; set { _compressionEnabled = value; RebuildSerializer(); } }
        public bool   RollbackEnabled    { get => _rollbackEnabled;    set => _rollbackEnabled  = value; }

        // ---------- lifecycle ----------

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            ResolveSaveDir();
            RebuildSerializer();
            RebuildEncryption();
            _rollback = new RollbackService(_maxSnapshots, _saveDir, _serializer, _encryption);
            _autoSave = new AutoSaveService(_autoSaveConfig, IsAnyDirty,
                (slot, mode, screenshot, screenshotName) =>
                    StartCoroutine(SaveCoroutine(slot, mode, screenshot, screenshotName, isAuto: true, callerSkipFrames: 4)));

            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update() => _autoSave?.Tick(Time.unscaledDeltaTime);

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) SaveRegistry.Clear();
        }

        private void ResolveSaveDir()
        {
            if (!string.IsNullOrEmpty(_customSavePath))
            {
                _saveDir = _customSavePath;
            }
            else
            {
#if UNITY_EDITOR
                _saveDir = _useAssetsFolderInEditor
                    ? Path.Combine(Application.dataPath, "SaveData~")
                    : Application.persistentDataPath;
#else
                _saveDir = Application.persistentDataPath;
#endif
            }
            Directory.CreateDirectory(_saveDir);
        }

        private void RebuildSerializer() => _serializer = new SaveSerializer(_compressionEnabled);
        private void RebuildEncryption() => _encryption = _encryptionEnabled ? new EncryptionService() : null;

        // ============================================================
        // ======================= PUBLIC API =========================
        // ============================================================

        // ---- Mode 1: тільки стан помічених обджектів ----
        public void Save(string slotId, bool captureScreenshot = false, string screenshotName = null)
            => StartCoroutine(SaveCoroutine(slotId, SaveMode.State, captureScreenshot, screenshotName, isAuto: false, callerSkipFrames: 3));

        // ---- Mode 2: вся сцена ----
        public void SaveFullScene(string slotId, bool captureScreenshot = false, string screenshotName = null)
            => StartCoroutine(SaveCoroutine(slotId, SaveMode.FullScene, captureScreenshot, screenshotName, isAuto: false, callerSkipFrames: 3));

        // ---- AutoSave: програмний запуск (не плутати з Inspector-конфігом) ----
        public void StartAutoSave(AutoSaveConfig config)
        {
            _autoSaveConfig = config;
            _autoSave = new AutoSaveService(config, IsAnyDirty,
                (slot, mode, screenshot, screenshotName) =>
                    StartCoroutine(SaveCoroutine(slot, mode, screenshot, screenshotName, isAuto: true, callerSkipFrames: 4)));
        }

        public void TriggerAutoSave() => _autoSave?.Trigger();

        // ---- Load ----
        public void Load(string slotId) => LoadInternal(slotId, callerSkipFrames: 3);

        // ---- Rollback ----
        public bool Rollback(string slotId, int stepsBack = 1)
        {
            if (!_rollbackEnabled || _rollback == null) return false;
            var file = _rollback.Rollback(slotId, stepsBack);
            if (file == null) return false;
            ApplyFile(file);
            LogOp(SaveOperationKind.Rollback, slotId, file.Mode, null, 0, true, true, true, file.Version, "Rolled back from snapshot", 3);
            return true;
        }

        // ---- helpers ----
        public string SlotPath(string slotId) => Path.Combine(_saveDir, slotId + ".sav");
        public string MetaPath(string slotId) => Path.Combine(_saveDir, slotId + ".savmeta");
        public string BakPath(string slotId)  => SlotPath(slotId) + ".bak";

        private bool IsAnyDirty()
        {
            // Беремо хеши останнього успішного save для слота autosave (як референс)
            string slot = _autoSaveConfig != null ? _autoSaveConfig.SlotId : "autosave";
            _lastHashes.TryGetValue(slot, out var prev);
            return SaveRegistry.AnyDirty(prev);
        }

        // ============================================================
        // ======================= SAVE FLOW ==========================
        // ============================================================

        private IEnumerator SaveCoroutine(string slotId, SaveMode mode, bool captureScreenshot, string screenshotName,
                                          bool isAuto, int callerSkipFrames)
        {
            var sw = Stopwatch.StartNew();

            // 1. Скріншот (опційно). Тільки якщо кодер увімкнув + до основної серіалізації.
            if (captureScreenshot)
            {
                string shotName = string.IsNullOrEmpty(screenshotName) ? slotId : screenshotName;
                yield return ScreenshotService.CaptureAndSave(_saveDir, shotName);
                LogOp(SaveOperationKind.ScreenshotCapture, slotId, mode, Path.Combine(_saveDir, shotName + ".jpg"),
                      0, true, false, false, _currentVersion, "Screenshot saved", callerSkipFrames + 1);
            }

            // 2. Будуємо SaveFile
            var file = new SaveFile
            {
                Version      = _currentVersion,
                TimestampUtc = DateTime.UtcNow.Ticks,
                SceneName    = SceneManager.GetActiveScene().name,
                SlotId       = slotId,
                Mode         = mode,
            };

            // Попередні хеші для пропуску незмінених
            _lastHashes.TryGetValue(slotId, out var prevHashes);
            // Кеш попередньо завантаженого файлу — щоб не читати багаторазово
            SaveFile prevFile = null;

            // 2a. Mode 1: збираємо ISaveable
            foreach (var sv in SaveRegistry.All)
            {
                ulong hash = sv.ComputeStateHash();
                file.Hashes[sv.SaveId] = hash;

                bool unchanged = prevHashes != null
                                 && prevHashes.TryGetValue(sv.SaveId, out var oldHash)
                                 && oldHash == hash;

                if (unchanged)
                {
                    // Пропускаємо OnSave — беремо таблицю з попереднього файлу
                    if (prevFile == null) prevFile = TryReadExisting(slotId);
                    if (prevFile != null && prevFile.Tables.TryGetValue(sv.SaveId, out var prevTable))
                        file.Tables[sv.SaveId] = prevTable;
                    else
                        file.Tables[sv.SaveId] = sv.OnSave(); // нема попереднього — зберігаємо
                }
                else
                {
                    file.Tables[sv.SaveId] = sv.OnSave();
                }
            }

            // 2b. Mode 2: знімок усієї сцени
            if (mode == SaveMode.FullScene)
            {
                file.Scene = SceneCapture.Capture(_prefabRegistry);
            }

            // 3. Серіалізація
            byte[] bytes;
            try { bytes = _serializer.Serialize(file); }
            catch (Exception e)
            {
                LogFail(SaveOperationKind.Save, slotId, mode, callerSkipFrames + 1, e); yield break;
            }
            if (_compressionEnabled)
                LogOp(SaveOperationKind.Compression, slotId, mode, null, bytes.Length, true, false, true, _currentVersion,
                      "MessagePack + LZ4", callerSkipFrames + 1);

            // 4. Шифрування
            if (_encryption != null)
            {
                try { bytes = _encryption.Encrypt(bytes); }
                catch (Exception e) { LogFail(SaveOperationKind.Encryption, slotId, mode, callerSkipFrames + 1, e); yield break; }
                LogOp(SaveOperationKind.Encryption, slotId, mode, null, bytes.Length, true, true, _compressionEnabled, _currentVersion,
                      "AES-256-CBC + HMAC-SHA256", callerSkipFrames + 1);
            }

            // 5. Атомарний запис
            string slotPath = SlotPath(slotId);
            try { WriteAtomic(slotPath, bytes); }
            catch (Exception e) { LogFail(SaveOperationKind.Save, slotId, mode, callerSkipFrames + 1, e); yield break; }

            // 6. Meta (без шифрування, для UI)
            var meta = new SlotMeta
            {
                SlotId         = slotId,
                TimestampUtc   = file.TimestampUtc,
                SceneName      = file.SceneName,
                Version        = file.Version,
                Mode           = file.Mode,
                ScreenshotName = captureScreenshot ? (screenshotName ?? slotId) + ".jpg" : null,
                FileSizeBytes  = bytes.Length,
                Encrypted      = _encryption != null,
                Compressed     = _compressionEnabled,
            };
            File.WriteAllBytes(MetaPath(slotId), _serializer.SerializeMeta(meta));

            // 7. Rollback
            if (_rollbackEnabled) _rollback?.Push(slotId, file);

            // 8. Запам'ятовуємо хеши
            _lastHashes[slotId] = new Dictionary<string, ulong>(file.Hashes);

            sw.Stop();
            LogOp(isAuto ? SaveOperationKind.AutoSave : SaveOperationKind.Save,
                  slotId, mode, slotPath, bytes.Length, true,
                  _encryption != null, _compressionEnabled, file.Version,
                  isAuto ? "AutoSave" : "Manual save",
                  callerSkipFrames + 1, durationMs: sw.Elapsed.TotalMilliseconds,
                  dataPreview: BuildDataPreview(file));
        }

        private SaveFile TryReadExisting(string slotId)
        {
            try
            {
                string path = SlotPath(slotId);
                if (!File.Exists(path)) return null;
                byte[] data = File.ReadAllBytes(path);
                if (_encryption != null) data = _encryption.Decrypt(data);
                return _serializer.Deserialize(data);
            }
            catch { return null; }
        }

        // ============================================================
        // ======================= LOAD FLOW ==========================
        // ============================================================

        private void LoadInternal(string slotId, int callerSkipFrames)
        {
            var sw = Stopwatch.StartNew();
            string path = SlotPath(slotId);

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[BitSaveSystem] Slot '{slotId}' not found.");
                return;
            }

            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (Exception e) { LogFail(SaveOperationKind.Load, slotId, default, callerSkipFrames + 1, e); return; }

            // Розшифрування з fallback на .bak
            if (_encryption != null)
            {
                try
                {
                    bytes = _encryption.Decrypt(bytes);
                    LogOp(SaveOperationKind.Decryption, slotId, default, null, bytes.Length, true, true, _compressionEnabled, _currentVersion,
                          "Decrypted main file", callerSkipFrames + 1);
                }
                catch (CryptographicException)
                {
                    string bak = BakPath(slotId);
                    if (File.Exists(bak))
                    {
                        Debug.LogWarning("[BitSaveSystem] Main file corrupted. Falling back to .bak");
                        try { bytes = _encryption.Decrypt(File.ReadAllBytes(bak)); }
                        catch (Exception e) { LogFail(SaveOperationKind.Load, slotId, default, callerSkipFrames + 1, e); return; }
                    }
                    else
                    {
                        LogFail(SaveOperationKind.Load, slotId, default, callerSkipFrames + 1, new Exception("Tampered file, no .bak"));
                        return;
                    }
                }
            }

            SaveFile file;
            try { file = _serializer.Deserialize(bytes); }
            catch (Exception e) { LogFail(SaveOperationKind.Load, slotId, default, callerSkipFrames + 1, e); return; }

            // Міграція
            if (file.Version < _currentVersion)
            {
                file = MigrationManager.Migrate(file, _currentVersion);
                LogOp(SaveOperationKind.Migration, slotId, file.Mode, null, 0, true, false, false, file.Version,
                      $"Migrated → v{_currentVersion}", callerSkipFrames + 1);
            }

            ApplyFile(file);

            // Кешуємо хеши
            _lastHashes[slotId] = new Dictionary<string, ulong>(file.Hashes ?? new Dictionary<string, ulong>());

            sw.Stop();
            LogOp(SaveOperationKind.Load, slotId, file.Mode, path, bytes.Length, true,
                  _encryption != null, _compressionEnabled, file.Version,
                  "Loaded", callerSkipFrames + 1, durationMs: sw.Elapsed.TotalMilliseconds,
                  dataPreview: BuildDataPreview(file));
        }

        private void ApplyFile(SaveFile file)
        {
            // Mode 2 — спочатку реконструюємо сцену, потім розливаємо ISaveable
            if (file.Mode == SaveMode.FullScene && file.Scene != null)
                SceneRestore.Apply(file.Scene, _prefabRegistry, destroyMissing: true);

            // Mode 1: даємо ISaveable свої таблиці
            foreach (var kv in file.Tables)
            {
                var sv = SaveRegistry.Find(kv.Key);
                if (sv != null) sv.OnLoad(kv.Value);
            }
        }

        // ============================================================
        // =================== ATOMIC FILE WRITE ======================
        // ============================================================

        private static void WriteAtomic(string path, byte[] data)
        {
            string tmp = path + ".tmp";
            string bak = path + ".bak";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Replace(tmp, path, bak); // atomic
            else                    File.Move(tmp, path);
        }

        // ============================================================
        // ====================== DIAGNOSTICS =========================
        // ============================================================

        private void LogOp(SaveOperationKind op, string slot, SaveMode mode, string path, long bytes,
                           bool ok, bool enc, bool cmp, int ver, string note, int callerSkip,
                           double durationMs = 0, string dataPreview = null)
        {
            var (call, stack) = SaveLog.CaptureCallSite(callerSkip);
            SaveLog.Add(new SaveLogEntry
            {
                Timestamp = DateTime.Now,
                Operation = op,
                SlotId = slot,
                Mode = mode,
                FilePath = path,
                FileSizeBytes = bytes,
                CallSite = call,
                Stack = stack,
                Encrypted = enc,
                Compressed = cmp,
                Version = ver,
                Note = note,
                Success = ok,
                DurationMs = durationMs,
                DataPreview = dataPreview,
            });
        }

        private void LogFail(SaveOperationKind op, string slot, SaveMode mode, int callerSkip, Exception e)
        {
            var (call, stack) = SaveLog.CaptureCallSite(callerSkip);
            SaveLog.Add(new SaveLogEntry
            {
                Timestamp = DateTime.Now,
                Operation = op, SlotId = slot, Mode = mode,
                CallSite = call, Stack = stack,
                Success = false, Error = e.Message,
            });
            Debug.LogError($"[BitSaveSystem] {op} '{slot}' failed: {e}");
        }

        private string BuildDataPreview(SaveFile file)
        {
#if UNITY_EDITOR
            try
            {
                var bytes = _serializer.Serialize(file);
                return _serializer.ToDebugJson(bytes);
            }
            catch { return null; }
#else
            return null;
#endif
        }
    }
}
