namespace BitSaveSystem
{
    /// <summary>
    /// Контракт для будь-якого об'єкта, стан якого зберігається у Mode 1
    /// ("зберегти стан помічених обджектів").
    /// </summary>
    public interface ISaveable
    {
        /// <summary>Стабільний ID між сесіями. Не <c>GetInstanceID()</c>!</summary>
        string SaveId { get; }

        /// <summary>
        /// Хеш поточного стану (FNV-1a по серіалізованих байтах усіх Track-полів).
        /// SaveManager порівнює цей хеш зі збереженим у попередньому файлі —
        /// якщо співпадає, об'єкт вважається незміненим і пропускається.
        /// Source Generator реалізує цей метод автоматично.
        /// </summary>
        ulong ComputeStateHash();

        /// <summary>Будує SaveTable з усіма полями стану.</summary>
        SaveTable OnSave();

        /// <summary>Відновлює стан з SaveTable.</summary>
        void OnLoad(SaveTable table);
    }
}
