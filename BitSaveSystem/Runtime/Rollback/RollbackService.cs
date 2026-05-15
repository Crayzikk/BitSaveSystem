using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Тримає N останніх знімків кожного слота. Зберігає і в пам'яті, і на диску
    /// (slotId_snap_{index}.sav). Використовується для відкату при пошкодженому файлі
    /// або за бажанням гравця.
    /// </summary>
    public sealed class RollbackService
    {
        private readonly int _maxSnapshots;
        private readonly string _dir;
        private readonly SaveSerializer _serializer;
        private readonly EncryptionService _encryption; // може бути null
        private readonly Dictionary<string, Queue<byte[]>> _byslot = new();

        public RollbackService(int maxSnapshots, string dir, SaveSerializer ser, EncryptionService enc)
        {
            _maxSnapshots = Mathf.Max(1, maxSnapshots);
            _dir = dir;
            _serializer = ser;
            _encryption = enc;
            LoadFromDisk();
        }

        public void Push(string slotId, SaveFile file)
        {
            if (!_byslot.TryGetValue(slotId, out var queue))
                _byslot[slotId] = queue = new Queue<byte[]>();

            byte[] bytes = _serializer.Serialize(file);
            if (_encryption != null) bytes = _encryption.Encrypt(bytes);
            queue.Enqueue(bytes);

            while (queue.Count > _maxSnapshots) queue.Dequeue();

            FlushToDisk(slotId, queue);
        }

        public SaveFile Rollback(string slotId, int stepsBack = 1)
        {
            if (!_byslot.TryGetValue(slotId, out var queue) || queue.Count == 0) return null;
            stepsBack = Mathf.Clamp(stepsBack, 1, queue.Count);
            var arr = queue.ToArray();
            byte[] bytes = arr[arr.Length - stepsBack];
            if (_encryption != null) bytes = _encryption.Decrypt(bytes);
            return _serializer.Deserialize(bytes);
        }

        public int GetSnapshotCount(string slotId)
            => _byslot.TryGetValue(slotId, out var q) ? q.Count : 0;

        private void FlushToDisk(string slotId, Queue<byte[]> queue)
        {
            int i = 0;
            foreach (var bytes in queue)
            {
                var path = Path.Combine(_dir, $"{slotId}_snap_{i}.sav");
                Directory.CreateDirectory(_dir);
                File.WriteAllBytes(path, bytes);
                i++;
            }
            // Видалити старі snapshot-файли поза межами queue.Count
            for (int j = i; j < _maxSnapshots; j++)
            {
                var p = Path.Combine(_dir, $"{slotId}_snap_{j}.sav");
                if (File.Exists(p)) File.Delete(p);
            }
        }

        private void LoadFromDisk()
        {
            if (!Directory.Exists(_dir)) return;
            foreach (var path in Directory.GetFiles(_dir, "*_snap_*.sav"))
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int sep = name.LastIndexOf("_snap_", System.StringComparison.Ordinal);
                if (sep < 0) continue;
                string slotId = name.Substring(0, sep);
                if (!_byslot.TryGetValue(slotId, out var q)) _byslot[slotId] = q = new Queue<byte[]>();
                q.Enqueue(File.ReadAllBytes(path));
            }
        }
    }
}
