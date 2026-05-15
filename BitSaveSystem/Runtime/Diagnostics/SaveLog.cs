using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace BitSaveSystem.Diagnostics
{
    public enum SaveOperationKind
    {
        Save, Load, AutoSave, Rollback, ScreenshotCapture, Migration, Encryption, Decryption, Compression, Decompression
    }

    /// <summary>Один запис у журналі для UI-вікна редактора.</summary>
    [Serializable]
    public sealed class SaveLogEntry
    {
        public DateTime  Timestamp;
        public SaveOperationKind Operation;
        public string    SlotId;
        public SaveMode  Mode;
        public string    FilePath;
        public long      FileSizeBytes;
        public string    CallSite;        // "Class.Method (file:line)"
        public string    Stack;           // повний стек для деталей
        public bool      Encrypted;
        public bool      Compressed;
        public int       Version;
        public string    DataPreview;     // JSON-превʼю (для Editor)
        public string    Note;
        public bool      Success = true;
        public string    Error;
        public double    DurationMs;

        public override string ToString()
            => $"[{Timestamp:HH:mm:ss.fff}] {Operation} {SlotId} ({Mode}) — {(Success ? "OK" : "FAIL")} — {DurationMs:0.0} ms";
    }

    /// <summary>
    /// Кільцевий буфер записів. Слухач (Editor-вікно) підписується на OnEntryAdded.
    /// </summary>
    public static class SaveLog
    {
        public static int Capacity = 500;
        private static readonly LinkedList<SaveLogEntry> _entries = new();

        public static event Action<SaveLogEntry> OnEntryAdded;

        public static IReadOnlyCollection<SaveLogEntry> All => _entries;

        public static void Add(SaveLogEntry entry)
        {
            _entries.AddLast(entry);
            while (_entries.Count > Capacity) _entries.RemoveFirst();
            OnEntryAdded?.Invoke(entry);
        }

        public static void Clear() => _entries.Clear();

        /// <summary>Повертає інформацію про call-site з пропуском N кадрів стеку.</summary>
        public static (string callSite, string stack) CaptureCallSite(int skipFrames)
        {
            var st = new StackTrace(skipFrames + 1, true);
            string call = "?";
            var frame = st.GetFrame(0);
            if (frame != null)
            {
                var m = frame.GetMethod();
                string file = frame.GetFileName();
                int line = frame.GetFileLineNumber();
                call = m == null
                    ? "?"
                    : $"{m.DeclaringType?.Name}.{m.Name}" + (file != null ? $" ({System.IO.Path.GetFileName(file)}:{line})" : "");
            }

            // Повний стек — короткий, без шумних кадрів
            var lines = new List<string>();
            for (int i = 0; i < st.FrameCount; i++)
            {
                var f = st.GetFrame(i);
                var m = f?.GetMethod();
                if (m == null) continue;
                lines.Add($"{m.DeclaringType?.Name}.{m.Name}");
            }
            return (call, string.Join(" → ", lines.Take(8)));
        }
    }
}
