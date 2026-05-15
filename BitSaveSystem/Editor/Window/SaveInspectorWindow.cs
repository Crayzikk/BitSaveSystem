using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitSaveSystem;
using BitSaveSystem.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace BitSaveSystem.EditorTools
{
    /// <summary>
    /// Window → Save System → Inspector
    ///
    /// Що показує:
    ///   - Список файлів у папці збереження (з мета-даними).
    ///   - Журнал усіх операцій (Save/Load/AutoSave/Rollback/Encryption/...).
    ///   - Звідки викликалось (CallSite + стек).
    ///   - Куди і коли збережено.
    ///   - Які маніпуляції виконано (шифр, стиснення, версія).
    ///   - Превʼю серіалізованих даних (JSON, конвертований із MessagePack).
    ///
    /// Має рядок пошуку і три фільтри: Operation, Slot, Mode.
    /// </summary>
    public sealed class SaveInspectorWindow : EditorWindow
    {
        [MenuItem("Window/Save System/Inspector")]
        public static void Open() => GetWindow<SaveInspectorWindow>("Save Inspector");

        private Vector2 _scrollLog;
        private Vector2 _scrollDetails;
        private Vector2 _scrollFiles;

        private SaveLogEntry _selected;
        private string _search = "";
        private SaveOperationKind? _filterOp;
        private string _filterSlot = "";
        private SaveMode? _filterMode;

        private bool _showFiles = true;
        private bool _showLog   = true;

        private void OnEnable()  => SaveLog.OnEntryAdded += OnEntry;
        private void OnDisable() => SaveLog.OnEntryAdded -= OnEntry;
        private void OnEntry(SaveLogEntry e) => Repaint();

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.Space();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(position.width * 0.55f)))
                {
                    DrawFiles();
                    EditorGUILayout.Space();
                    DrawLog();
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    DrawDetails();
                }
            }
        }

        // ---------- toolbar ----------

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70))) Repaint();
                if (GUILayout.Button("Clear log", EditorStyles.toolbarButton, GUILayout.Width(80))) SaveLog.Clear();
                if (GUILayout.Button("Open folder", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    if (SaveManager.Instance != null) EditorUtility.RevealInFinder(SaveManager.Instance.SaveDir);
                }
                GUILayout.FlexibleSpace();
                _showFiles = GUILayout.Toggle(_showFiles, "Files", EditorStyles.toolbarButton, GUILayout.Width(60));
                _showLog   = GUILayout.Toggle(_showLog,   "Log",   EditorStyles.toolbarButton, GUILayout.Width(60));
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Search:", GUILayout.Width(50));
                _search = EditorGUILayout.TextField(_search);

                GUILayout.Label("Op:", GUILayout.Width(25));
                _filterOp = DrawNullableEnum(_filterOp);

                GUILayout.Label("Slot:", GUILayout.Width(35));
                _filterSlot = EditorGUILayout.TextField(_filterSlot, GUILayout.Width(80));

                GUILayout.Label("Mode:", GUILayout.Width(40));
                _filterMode = DrawNullableMode(_filterMode);

                if (GUILayout.Button("X", GUILayout.Width(20))) { _search = _filterSlot = ""; _filterOp = null; _filterMode = null; }
            }
        }

        private SaveOperationKind? DrawNullableEnum(SaveOperationKind? cur)
        {
            var names = new List<string> { "(any)" };
            names.AddRange(Enum.GetNames(typeof(SaveOperationKind)));
            int idx = cur.HasValue ? (int)cur.Value + 1 : 0;
            int newIdx = EditorGUILayout.Popup(idx, names.ToArray(), GUILayout.Width(120));
            return newIdx == 0 ? null : (SaveOperationKind?)(newIdx - 1);
        }

        private SaveMode? DrawNullableMode(SaveMode? cur)
        {
            var names = new[] { "(any)", "State", "FullScene" };
            int idx = cur.HasValue ? (int)cur.Value + 1 : 0;
            int newIdx = EditorGUILayout.Popup(idx, names, GUILayout.Width(90));
            return newIdx == 0 ? null : (SaveMode?)(newIdx - 1);
        }

        // ---------- files ----------

        private void DrawFiles()
        {
            if (!_showFiles) return;
            EditorGUILayout.LabelField("Save files", EditorStyles.boldLabel);
            if (SaveManager.Instance == null)
            {
                EditorGUILayout.HelpBox("SaveManager not found in scene.", MessageType.Info);
                return;
            }

            var dir = SaveManager.Instance.SaveDir;
            if (!Directory.Exists(dir))
            {
                EditorGUILayout.HelpBox($"Folder does not exist: {dir}", MessageType.Warning);
                return;
            }

            _scrollFiles = EditorGUILayout.BeginScrollView(_scrollFiles, GUILayout.MaxHeight(150));
            foreach (var path in Directory.GetFiles(dir).OrderBy(p => p))
            {
                var fi = new FileInfo(path);
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    EditorGUILayout.LabelField(fi.Name, GUILayout.Width(220));
                    EditorGUILayout.LabelField(SizeStr(fi.Length), GUILayout.Width(70));
                    EditorGUILayout.LabelField(fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"), GUILayout.Width(150));
                    if (GUILayout.Button("Reveal", GUILayout.Width(60))) EditorUtility.RevealInFinder(path);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // ---------- log ----------

        private void DrawLog()
        {
            if (!_showLog) return;
            EditorGUILayout.LabelField($"Log entries ({SaveLog.All.Count})", EditorStyles.boldLabel);
            _scrollLog = EditorGUILayout.BeginScrollView(_scrollLog);

            foreach (var e in SaveLog.All.Reverse())
            {
                if (!PassFilter(e)) continue;
                Color c = e.Success ? Color.white : new Color(1f, 0.6f, 0.6f);
                var prev = GUI.color; GUI.color = c;
                using (new EditorGUILayout.HorizontalScope("box"))
                {
                    if (GUILayout.Button(e.ToString(), EditorStyles.label))
                    {
                        _selected = e;
                        Repaint();
                    }
                }
                GUI.color = prev;
            }
            EditorGUILayout.EndScrollView();
        }

        private bool PassFilter(SaveLogEntry e)
        {
            if (_filterOp.HasValue   && e.Operation != _filterOp.Value)   return false;
            if (_filterMode.HasValue && e.Mode      != _filterMode.Value) return false;
            if (!string.IsNullOrEmpty(_filterSlot) && (e.SlotId == null || !e.SlotId.Contains(_filterSlot, StringComparison.OrdinalIgnoreCase))) return false;
            if (!string.IsNullOrEmpty(_search))
            {
                string hay = $"{e.SlotId} {e.CallSite} {e.Note} {e.FilePath} {e.Error}".ToLowerInvariant();
                if (!hay.Contains(_search.ToLowerInvariant())) return false;
            }
            return true;
        }

        // ---------- details ----------

        private void DrawDetails()
        {
            EditorGUILayout.LabelField("Details", EditorStyles.boldLabel);
            if (_selected == null) { EditorGUILayout.HelpBox("Select an entry on the left.", MessageType.None); return; }

            _scrollDetails = EditorGUILayout.BeginScrollView(_scrollDetails);

            DrawRow("Time",          _selected.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            DrawRow("Operation",     _selected.Operation.ToString());
            DrawRow("Mode",          _selected.Mode.ToString());
            DrawRow("Slot",          _selected.SlotId);
            DrawRow("Version",       _selected.Version.ToString());
            DrawRow("Success",       _selected.Success.ToString());
            if (_selected.DurationMs > 0) DrawRow("Duration", $"{_selected.DurationMs:0.00} ms");
            DrawRow("File",          _selected.FilePath);
            DrawRow("Size",          SizeStr(_selected.FileSizeBytes));
            DrawRow("Encrypted",     _selected.Encrypted.ToString());
            DrawRow("Compressed",    _selected.Compressed.ToString());
            DrawRow("Note",          _selected.Note);
            if (!string.IsNullOrEmpty(_selected.Error)) DrawRow("Error", _selected.Error);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Call site", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(_selected.CallSite ?? "—", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.LabelField("Stack", EditorStyles.miniBoldLabel);
            EditorGUILayout.SelectableLabel(_selected.Stack ?? "—", EditorStyles.wordWrappedMiniLabel,
                                             GUILayout.Height(EditorGUIUtility.singleLineHeight * 3));

            if (!string.IsNullOrEmpty(_selected.DataPreview))
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Data preview (JSON)", EditorStyles.miniBoldLabel);
                EditorGUILayout.TextArea(_selected.DataPreview, GUILayout.MinHeight(200));
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawRow(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label, GUILayout.Width(90));
                EditorGUILayout.SelectableLabel(value ?? "—", GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
        }

        private static string SizeStr(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes / (1024.0 * 1024):0.0} MB";
        }
    }
}
