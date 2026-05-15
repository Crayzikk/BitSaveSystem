using System.Collections.Generic;
using BitSaveSystem;
using UnityEngine;

namespace BitSaveSystem.Demo
{
    /// <summary>
    /// BitSave System — Interactive Demo
    ///
    /// Drag this script onto an empty GameObject in a new scene, press Play.
    /// The script builds the demo scene at runtime (floor, lighting, camera,
    /// player, chest, enemies) and shows an OnGUI control panel.
    ///
    /// What is demonstrated:
    ///   • Mode 1 — saving the state of marked objects (player, inventory, chest)
    ///   • Mode 2 — saving the entire scene including runtime-spawned enemies
    ///   • Autosave on a timer
    ///   • Rollback through the last N snapshots
    ///   • Screenshot bundled with the save file
    ///
    /// To explore the saved files: Window → Save System → Inspector
    /// </summary>
    public class BitSaveDemo : MonoBehaviour
    {
        private const string Slot = "demo_slot";
        private const float AutosaveInterval = 30f;

        private DemoPlayer    _player;
        private DemoInventory _inventory;
        private DemoChest     _chest;
        private List<DemoEnemy> _enemies = new();

        private SaveManager _saveManager;
        private bool   _autosaveEnabled;
        private float  _autosaveTimer;

        private string _status   = "Press buttons to begin.";
        private float  _statusUntil;

        // ============================================================
        //  Scene build
        // ============================================================
        private void Start()
        {
            EnsureSaveManager();
            BuildScene();
        }

        private void EnsureSaveManager()
        {
            _saveManager = FindObjectOfType<SaveManager>();
            if (_saveManager == null)
                _saveManager = new GameObject("[SaveManager]").AddComponent<SaveManager>();
        }

        private void BuildScene()
        {
            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
                cam.transform.SetPositionAndRotation(new Vector3(0, 7, -11),
                                                      Quaternion.Euler(28, 0, 0));
                cam.backgroundColor = new Color(0.15f, 0.18f, 0.22f);
            }

            if (FindObjectOfType<Light>() == null)
            {
                var light = new GameObject("Directional Light").AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                light.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            // Floor
            var floor = PrimitiveHelper.CreatePrimitive(PrimitiveType.Plane, new Color(0.4f, 0.4f, 0.45f));
            floor.name = "Floor";
            floor.transform.localScale = Vector3.one * 2.5f;

            // Player
            var playerGO = PrimitiveHelper.CreatePrimitive(PrimitiveType.Capsule, new Color(0.3f, 0.65f, 0.95f));
            playerGO.name = "Player";
            playerGO.transform.position = new Vector3(0, 1, 0);
            _player    = playerGO.AddComponent<DemoPlayer>();
            _inventory = playerGO.AddComponent<DemoInventory>();

            // Chest
            var chestGO = PrimitiveHelper.CreatePrimitive(PrimitiveType.Cube, new Color(0.55f, 0.40f, 0.20f));
            chestGO.name = "Chest";
            chestGO.transform.position = new Vector3(3, 0.5f, 2);
            _chest = chestGO.AddComponent<DemoChest>();

            // A couple of enemies to start with
            SpawnEnemy();
            SpawnEnemy();
        }

        // ============================================================
        //  Update — autosave timer + status fade
        // ============================================================
        private void Update()
        {
            if (_autosaveEnabled)
            {
                _autosaveTimer -= Time.deltaTime;
                if (_autosaveTimer <= 0)
                {
                    _autosaveTimer = AutosaveInterval;
                    _saveManager.Save(Slot);
                    SetStatus($"Autosaved to '{Slot}'");
                }
            }
        }

        // ============================================================
        //  Actions
        // ============================================================
        private void SpawnEnemy()
        {
            var color = new Color(0.9f, 0.3f + Random.value * 0.2f, 0.3f);
            var go = PrimitiveHelper.CreatePrimitive(PrimitiveType.Cube, color);
            go.name = "Enemy";
            go.transform.position = new Vector3(Random.Range(-5f, 5f), 0.5f, Random.Range(-5f, 5f));
            _enemies.Add(go.AddComponent<DemoEnemy>());
        }

        private void KillRandomEnemy()
        {
            _enemies.RemoveAll(e => e == null);
            if (_enemies.Count == 0) return;
            int idx = Random.Range(0, _enemies.Count);
            Destroy(_enemies[idx].gameObject);
            _enemies.RemoveAt(idx);
        }

        private void RescanEnemies()
        {
            _enemies.Clear();
            _enemies.AddRange(FindObjectsOfType<DemoEnemy>());
        }

        // ============================================================
        //  OnGUI panel
        // ============================================================
        private GUIStyle _hStyle, _statusStyle;

        private Vector2 _scroll;

        private void OnGUI()
        {
            BuildStyles();

            const float W = 300;
            GUILayout.BeginArea(new Rect(20, 20, W, Screen.height - 40), GUI.skin.box);

            GUILayout.Label("BitSave System Demo", _hStyle);

            DrawState();
            GUILayout.Space(10);

            // ScrollView — щоб усі кнопки помістились на будь-якій висоті екрана
            _scroll = GUILayout.BeginScrollView(_scroll);

            DrawSection("Change State");
            if (GUILayout.Button("Take Damage (−15 HP)")) _player.TakeDamage(15);
            if (GUILayout.Button("Heal (+20 HP)"))         _player.Heal(20);
            if (GUILayout.Button("Add Gold (+50)"))        _inventory.AddGold(50);
            if (GUILayout.Button("Add Random Item"))       _inventory.AddItem("Item_" + Random.Range(1, 999));
            if (GUILayout.Button("Toggle Chest"))          _chest.Toggle();
            if (GUILayout.Button("Spawn Enemy"))           SpawnEnemy();
            if (GUILayout.Button("Kill Random Enemy"))     KillRandomEnemy();

            GUILayout.Space(10);
            DrawSection("Save / Load");
            if (GUILayout.Button("Save (state only)"))
            {
                _saveManager.Save(Slot, captureScreenshot: true);
                SetStatus("State saved (Mode 1)");
            }
            if (GUILayout.Button("Save Full Scene"))
            {
                _saveManager.SaveFullScene(Slot, captureScreenshot: true);
                SetStatus("Full scene saved (Mode 2)");
            }
            if (GUILayout.Button("Load"))
            {
                _saveManager.Load(Slot);
                RescanEnemies();
                SetStatus("Loaded");
            }
            if (GUILayout.Button("Rollback 1 step"))
            {
                bool ok = _saveManager.Rollback(Slot, 1);
                RescanEnemies();
                SetStatus(ok ? "Rolled back" : "No snapshots yet");
            }

            GUILayout.Space(10);
            DrawSection("Autosave");
            string autoLabel = _autosaveEnabled
                ? $"Disable Autosave ({_autosaveTimer:0}s left)"
                : "Enable Autosave (every 30s)";
            if (GUILayout.Button(autoLabel))
            {
                _autosaveEnabled = !_autosaveEnabled;
                _autosaveTimer   = AutosaveInterval;
                SetStatus(_autosaveEnabled ? "Autosave ON" : "Autosave OFF");
            }

            GUILayout.EndScrollView();

            DrawStatus();

            GUILayout.EndArea();
        }

        private void DrawState()
        {
            GUILayout.Space(4);
            GUILayout.Label($"HP:        {_player.Hp:0} / {_player.MaxHp:0}");
            GUILayout.Label($"Gold:      {_inventory.Gold}");
            GUILayout.Label($"Items:     {_inventory.ItemCount}");
            GUILayout.Label($"Chest:     {(_chest.IsOpen ? "OPEN" : "closed")}");
            GUILayout.Label($"Enemies:   {CountEnemies()}");
        }

        private int CountEnemies()
        {
            _enemies.RemoveAll(e => e == null);
            return _enemies.Count;
        }

        private void DrawSection(string title)
        {
            GUILayout.Label(title, _hStyle);
        }

        private void DrawStatus()
        {
            if (string.IsNullOrEmpty(_status)) return;
            float alpha = _statusUntil > 0
                ? Mathf.Clamp01((_statusUntil - Time.unscaledTime) / 0.8f + 0.2f)
                : 1f;
            var col = GUI.color;
            GUI.color = new Color(1, 1, 1, alpha);
            GUILayout.Label(_status, _statusStyle);
            GUI.color = col;
        }

        private void SetStatus(string msg)
        {
            _status      = msg;
            _statusUntil = Time.unscaledTime + 3f;
            Debug.Log("[BitSave Demo] " + msg);
        }

        private void BuildStyles()
        {
            if (_hStyle == null)
            {
                _hStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    fontSize  = 13,
                };
            }
            if (_statusStyle == null)
            {
                _statusStyle = new GUIStyle(GUI.skin.box)
                {
                    fontStyle = FontStyle.Italic,
                    wordWrap  = true,
                };
            }
        }
    }

    // =============================================================================
    //  Demo entities — minimal examples of three integration patterns
    // =============================================================================

    /// <summary>
    /// Pattern A — using [Saveable] + [Track] (Source Generator does the rest).
    /// </summary>
    [Saveable("demo_player")]
    public partial class DemoPlayer : MonoBehaviour
    {
        [Track] public float Hp      = 100f;
        [Track] public float MaxHp   = 100f;
        [Track] public bool  IsAlive = true;

        public void TakeDamage(float amount)
        {
            Hp      = Mathf.Max(0, Hp - amount);
            IsAlive = Hp > 0;
        }

        public void Heal(float amount)
        {
            Hp      = Mathf.Min(MaxHp, Hp + amount);
            IsAlive = Hp > 0;
        }
    }

    /// <summary>
    /// Pattern B — manual implementation via SaveableBehaviour.
    /// </summary>
    public class DemoInventory : SaveableBehaviour
    {
        [SerializeField] private int          _gold;
        [SerializeField] private List<string> _items = new();

        public int Gold      => _gold;
        public int ItemCount => _items.Count;

        public override string SaveId => "demo_inventory";

        public void AddGold(int amount) => _gold += amount;
        public void AddItem(string id)  => _items.Add(id);

        public override SaveTable OnSave() => SaveTable.From(this)
            .Set("gold",  _gold)
            .Set("items", _items);

        public override void OnLoad(SaveTable t)
        {
            _gold  = t.Get<int>("gold", 0);
            _items = t.Get<List<string>>("items", new List<string>());
        }
    }

    /// <summary>
    /// Pattern C — boolean flag plus a visual reaction in OnLoad.
    /// </summary>
    public class DemoChest : SaveableBehaviour
    {
        [SerializeField] private bool _isOpen;

        public bool IsOpen => _isOpen;

        public override string SaveId => "demo_chest";

        public void Toggle()
        {
            _isOpen = !_isOpen;
            UpdateVisual();
        }

        public override SaveTable OnSave() => SaveTable.From(this).Set("open", _isOpen);

        public override void OnLoad(SaveTable t)
        {
            _isOpen = t.Get<bool>("open", false);
            UpdateVisual();
        }

        private void UpdateVisual()
        {
            var r = GetComponent<Renderer>();
            if (r == null) return;
            r.material.color = _isOpen
                ? new Color(0.95f, 0.85f, 0.35f)   // gold (open)
                : new Color(0.55f, 0.40f, 0.20f);  // brown (closed)
        }
    }

    /// <summary>
    /// Demo enemy — pure marker. Has no state of its own,
    /// but Mode 2 preserves its existence in the scene.
    /// </summary>
    public class DemoEnemy : MonoBehaviour { }
}
