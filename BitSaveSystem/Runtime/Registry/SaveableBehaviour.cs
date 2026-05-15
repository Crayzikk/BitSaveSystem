using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Опціональний базовий клас. Якщо ваш MonoBehaviour може його успадкувати —
    /// реєстрація у <see cref="SaveRegistry"/> відбувається автоматично.
    ///
    /// Якщо успадкувати неможливо — реалізуйте <see cref="ISaveable"/> напряму
    /// і викликайте <c>SaveRegistry.Register/Unregister</c> у <c>OnEnable/OnDisable</c>.
    /// </summary>
    public abstract class SaveableBehaviour : MonoBehaviour, ISaveable
    {
        /// <summary>За замовчуванням — ім'я типу. Перевизначте для кількох інстансів.</summary>
        public virtual string SaveId => GetType().Name;

        public abstract SaveTable OnSave();
        public abstract void OnLoad(SaveTable table);

        /// <summary>
        /// За замовчуванням — хеш від OnSave().
        /// Source Generator при наявності [Saveable] перевизначить на швидший варіант
        /// (хеш по полях напряму, без створення SaveTable).
        /// </summary>
        public virtual ulong ComputeStateHash() => HashService.HashSaveTable(OnSave());

        protected virtual void OnEnable()  => SaveRegistry.Register(this);
        protected virtual void OnDisable() => SaveRegistry.Unregister(this);
    }
}
