using System;

namespace BitSaveSystem
{
    /// <summary>
    /// Позначає клас як такий, для якого Source Generator згенерує:
    /// - метод <c>ComputeStateHash()</c> (для Dirty-перевірки через хешування)
    /// - реєстрацію у <see cref="SaveRegistry"/> через partial OnEnable/OnDisable
    /// - реалізацію <see cref="ISaveable.OnSave"/> / <c>OnLoad</c> (якщо явно не реалізовані)
    ///
    /// Клас має бути <c>partial</c>. Поля для серіалізації позначаються
    /// <see cref="TrackAttribute"/> або просто включаються всі публічні
    /// поля, якщо в атрибуті виставлено <c>IncludeAllPublicFields = true</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class SaveableAttribute : Attribute
    {
        /// <summary>Стабільний рядковий ID. Якщо не вказано — буде використано ім'я типу.</summary>
        public string Id { get; }

        /// <summary>Якщо true — Source Generator включить ВСІ публічні поля/властивості,
        /// крім тих, що позначені <see cref="SaveIgnoreAttribute"/>.</summary>
        public bool IncludeAllPublicFields { get; set; } = false;

        public SaveableAttribute(string id = null) { Id = id; }
    }

    /// <summary>
    /// Позначає поле або властивість як таке, що його стан треба зберігати
    /// та враховувати під час Dirty-перевірки. Працює навіть якщо клас НЕ
    /// має <see cref="SaveableAttribute"/> — у такому разі генератор створить
    /// розширення, яке сам клас викличе у <c>OnSave</c>/<c>OnLoad</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property,
                    Inherited = false, AllowMultiple = false)]
    public sealed class TrackAttribute : Attribute
    {
        /// <summary>Власний ключ у SaveTable. Якщо null — береться ім'я поля.</summary>
        public string Key { get; }
        public TrackAttribute(string key = null) { Key = key; }
    }

    /// <summary>
    /// Виключає поле з автоматичного збереження, навіть якщо воно публічне
    /// і клас має <c>IncludeAllPublicFields = true</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property,
                    Inherited = false, AllowMultiple = false)]
    public sealed class SaveIgnoreAttribute : Attribute { }
}
