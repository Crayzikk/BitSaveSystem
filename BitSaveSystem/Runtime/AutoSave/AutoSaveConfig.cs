using UnityEngine;

namespace BitSaveSystem
{
    public enum AutoSaveMode { Disabled, Interval, OnTrigger, Hybrid }

    /// <summary>
    /// ScriptableObject з налаштуваннями автозбереження.
    /// Створюється: Assets → Create → BitSaveSystem → AutoSave Config.
    /// </summary>
    [CreateAssetMenu(menuName = "BitSaveSystem/AutoSave Config", fileName = "AutoSaveConfig")]
    public sealed class AutoSaveConfig : ScriptableObject
    {
        public AutoSaveMode Mode = AutoSaveMode.Interval;

        [Tooltip("Інтервал у секундах між автозбереженнями (5..3600).")]
        [Range(5f, 3600f)]
        public float IntervalSeconds = 60f;

        [Tooltip("Слот, у який пише AutoSave.")]
        public string SlotId = "autosave";

        [Tooltip("Що зберігати: тільки стан або всю сцену.")]
        public SaveMode SaveMode = SaveMode.State;

        [Tooltip("Скріншот разом з автозбереженням.")]
        public bool CaptureScreenshot = false;

        [Tooltip("Пропускати збереження, якщо нічого не змінилось.")]
        public bool SkipIfNothingChanged = true;
    }
}
