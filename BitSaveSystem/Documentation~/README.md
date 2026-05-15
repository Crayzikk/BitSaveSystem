# Save System

Модуль збереження ігрової ситуації для Unity. Імпортуйте у проєкт — використовуйте.

---

## Зміст

1. [Швидкий старт](#швидкий-старт)
2. [Два режими збереження](#два-режими-збереження)
3. [Налаштування SaveManager](#налаштування-savemanager)
4. [API: 4 методи збереження](#api-4-методи-збереження)
5. [Скріншоти](#скріншоти)
6. [Шифрування, стиснення, відкат](#шифрування-стиснення-відкат)
7. [Source Generator: дві форми](#source-generator-дві-форми)
8. [Mode 2: PrefabRegistry](#mode-2-prefabregistry)
9. [Editor Inspector Window](#editor-inspector-window)
10. [Версійність і міграція](#версійність-і-міграція)
11. [Файлова структура на диску](#файлова-структура-на-диску)
12. [Часті помилки](#часті-помилки)

---

## Швидкий старт

1. Імпортуйте папку `SaveSystem/` у `Assets/`.
2. Встановіть пакет MessagePack для C#: відкрийте Package Manager → Add from git URL:
   ```
   https://github.com/MessagePack-CSharp/MessagePack-CSharp.git#v2.5.124
   ```
   Або через NuGet for Unity. Потрібні: `MessagePack`, `MessagePack.Annotations`, `MessagePackAnalyzer`.
3. Створіть порожній GameObject у стартовій сцені, додайте компонент **SaveManager**.
4. (Опційно для Mode 2) Створіть `Assets/Resources/PrefabRegistry.asset` через
   *Create → SaveSystem → Prefab Registry* і прив'яжіть до SaveManager.
5. (Опційно) Скомпілюйте і додайте `SaveSystem.SourceGenerator.dll` як `RoslynAnalyzer`
   (див. `SourceGenerator/README.md`).

Готово. Тепер у будь-якому місці коду:

```csharp
SaveManager.Instance.Save("slot_1");
SaveManager.Instance.Load("slot_1");
```

---

## Два режими збереження

| Режим | API | Що зберігає | Коли використовувати |
|-------|-----|-------------|----------------------|
| **Mode 1: State** | `Save(slot)` | Тільки стан помічених `ISaveable` | Коли структура сцени фіксована, змінюються тільки атрибути об'єктів (HP гравця, інвентар, відкриті двері). |
| **Mode 2: FullScene** | `SaveFullScene(slot)` | Усі GameObjects сцени + їхні компоненти + стан | Коли потрібно відновити динамічну сцену цілком (вороги/трупи/підібрані предмети, відкриті скрині, тощо). Жодних позначок робити не треба. |

Mode 2 використовує **PrefabRegistry** для компактності: для кожного GameObject
шукається префаб, з якого він був інстансований. Якщо знайдено — пишеться лише
`prefabGuid + дельта змінених полів`. Якщо ні (об'єкт створено динамічно без префаба) —
fallback на повну рефлексію всіх компонентів.

---

## Налаштування SaveManager

В інспекторі компонента `SaveManager`:

| Поле | За замовчуванням | Опис |
|------|------------------|------|
| Custom Save Path | `""` | Якщо порожнє — `Application.persistentDataPath`. |
| Use Assets Folder In Editor | `true` | В Editor пише в `Assets/SaveData`. У білді ігнорується. |
| Encryption Enabled | `true` | AES-256-CBC + HMAC-SHA256 |
| Compression Enabled | `true` | MessagePack + LZ4 |
| Rollback Enabled | `true` | Зберігає N останніх знімків кожного слота |
| Max Snapshots | `5` | Розмір rollback-черги |
| Current Version | `1` | Версія формату. Підіймайте при змінах структури. |
| Prefab Registry | `null` | ScriptableObject з мапою префабів (для Mode 2) |
| Auto Save Config | `null` | ScriptableObject з налаштуваннями автозбереження |

Усі ці поля можна змінювати програмно через властивості `SaveManager.Instance.EncryptionEnabled`, тощо.

---

## API: 4 методи збереження

```csharp
// 1. Зберегти дані (Mode 1)
SaveManager.Instance.Save(slotId);
SaveManager.Instance.Save(slotId, captureScreenshot: true);
SaveManager.Instance.Save(slotId, captureScreenshot: true, screenshotName: "checkpoint_42");

// 2. Зберегти всю сцену (Mode 2)
SaveManager.Instance.SaveFullScene(slotId);
SaveManager.Instance.SaveFullScene(slotId, captureScreenshot: true);

// 3. Автозбереження даних: налаштовується через AutoSaveConfig.SaveMode = State
//    Працює автоматично за таймером; кодер задає інтервал.
//    Програмно:
var cfg = ScriptableObject.CreateInstance<AutoSaveConfig>();
cfg.Mode            = AutoSaveMode.Interval;
cfg.IntervalSeconds = 60f;
cfg.SaveMode        = SaveMode.State;
cfg.SlotId          = "autosave";
SaveManager.Instance.StartAutoSave(cfg);

// 4. Автозбереження всієї сцени: те саме, але cfg.SaveMode = SaveMode.FullScene
cfg.SaveMode = SaveMode.FullScene;
SaveManager.Instance.StartAutoSave(cfg);
```

Завантаження одне для всіх режимів:

```csharp
SaveManager.Instance.Load(slotId);
```

---

## Скріншоти

Скріншот робиться **тільки якщо** `captureScreenshot: true` (за замовчуванням `false`):

```csharp
SaveManager.Instance.Save("slot_1", captureScreenshot: true);
// → slot_1.jpg поруч із slot_1.sav

SaveManager.Instance.Save("slot_1", captureScreenshot: true, screenshotName: "boss_fight");
// → boss_fight.jpg
```

Реалізація: корутина `WaitForEndOfFrame` → `ScreenCapture.CaptureScreenshotAsTexture()`
→ `EncodeToJPG(60)` → файл (~50 КБ для 1080p).

---

## Шифрування, стиснення, відкат

Усе вмикається/вимикається через bool-поля у Inspector або програмно:

```csharp
SaveManager.Instance.EncryptionEnabled  = false;
SaveManager.Instance.CompressionEnabled = true;
SaveManager.Instance.RollbackEnabled    = true;
```

**Шифрування**: AES-256-CBC + HMAC-SHA256 (Encrypt-then-MAC). HMAC перевіряється
**до** розшифровки; при невідповідності — автоматичний fallback на `.bak`.
Ключі зберігаються в `PlayerPrefs` (`SS_EncKey_v3`, `SS_MacKey_v3`).
Альтернативно можна передати власні ключі (наприклад, з серверу) через конструктор
`new EncryptionService(encKey, macKey)`.

**Стиснення**: MessagePack + LZ4BlockArray. У 5–10 разів швидше за GZip.

**Відкат**: тримає N знімків (за замовчуванням 5) у черзі. Зберігаються і в пам'яті,
і на диску (`{slot}_snap_{0..N-1}.sav`). Виклик:

```csharp
SaveManager.Instance.Rollback("slot_1", stepsBack: 1); // повернутись на 1 збереження назад
```

---

## Source Generator: дві форми

### Форма 1: повний пакет через `[Saveable]` + `[Track]`

```csharp
[Saveable("player_health")]
public partial class PlayerHealth : MonoBehaviour
{
    [Track("hp")]    public float CurrentHp = 100f;
    [Track]          public bool  IsAlive   = true;  // ключ = "IsAlive"
    [SaveIgnore]     public float TempBuff;          // не зберігається
}
```

Генератор додасть: `SaveId`, `ComputeStateHash`, `OnSave`, `OnLoad`,
`OnEnable`/`OnDisable` з реєстрацією у `SaveRegistry`.

Альтернатива: `[Saveable(IncludeAllPublicFields = true)]` — без `[Track]`, всі публічні
поля автоматично, окрім `[SaveIgnore]`.

### Форма 2: тільки helper-методи через `[Track]`

```csharp
public partial class CustomLogic : MonoBehaviour, ISaveable
{
    [Track("score")] private int _score;

    public string SaveId => "custom_logic";
    public ulong  ComputeStateHash() => ComputeFieldsHash();  // згенеровано
    public SaveTable OnSave() {
        var t = SaveTable.From(this);
        WriteFieldsTo(t);                                       // згенеровано
        return t;
    }
    public void OnLoad(SaveTable t) => ReadFieldsFrom(t);       // згенеровано

    private void OnEnable()  => SaveRegistry.Register(this);
    private void OnDisable() => SaveRegistry.Unregister(this);
}
```

### Як працює Dirty-перевірка через хеш

При кожному `Save` для кожного `ISaveable` рахується `ComputeStateHash()`
(FNV-1a 64-bit по серіалізованих байтах усіх Track-полів). Якщо хеш збігається з
тим, що збережено в попередньому файлі, об'єкт **пропускається** — його SaveTable
береться з попереднього файлу без виклику `OnSave()`.

Це автоматично, без `Tracked<T>`. Працює навіть для колекцій — бо порівнюється
підсумковий хеш від серіалізованої колекції, а не сам факт виклику `Add()`.

---

## Mode 2: PrefabRegistry

PrefabRegistry — ScriptableObject, що мапить `prefabGuid → GameObject`. Заповнюється
автоматично:

- При імпорті будь-якого префаба `PrefabRegistryPostprocessor` додає його в реєстр.
- Перший раз: створіть `Assets/PrefabRegistry.asset` через меню *Create →
  SaveSystem → Prefab Registry*, інакше Postprocessor нічого не робитиме.

У білді (де `PrefabUtility` недоступний) для рантайм-інстансованих GameObject додавайте
компонент `PrefabSourceMarker` із заповненим `PrefabGuid`. Інакше Mode 2 збереже
такий GO через рефлексивний fallback (працює, але важче).

Для GameObject, що знаходяться в сцені від початку, потрібен компонент `SceneObjectId` —
він **автоматично додається** через `OnValidate()` у Editor.

---

## Editor Inspector Window

Меню: **Window → Save System → Inspector**.

Показує:
- Список файлів у папці збереження (з розміром і датою).
- Журнал усіх операцій: Save, Load, AutoSave, Rollback, ScreenshotCapture, Encryption,
  Decryption, Compression, Migration.
- Для кожного запису: час, slot, mode, файл, розмір, чи зашифровано/стиснено,
  версія, **call-site** (звідки викликалось), стек, тривалість, превʼю даних у JSON.

Фільтри: пошук по тексту, фільтр за operation, slot, mode.

---

## Версійність і міграція

```csharp
public class MigratorV1toV2 : IMigrator
{
    public int FromVersion => 1;
    public int ToVersion   => 2;
    public SaveFile Migrate(SaveFile file)
    {
        if (file.Tables.TryGetValue("player_health", out var t) && !t.Has("stamina"))
            t.Set("stamina", 100f);
        return file;
    }
}

// Десь у Bootstrap:
MigrationManager.Register(new MigratorV1toV2());
```

Підніміть `SaveManager.CurrentVersion` у Inspector і додайте відповідний мігратор.
Старі сейви автоматично пройдуть весь ланцюжок v1 → v2 → v3 при наступному завантаженні.

---

## Файлова структура на диску

```
{SaveDir}/
├── slot_1.sav             ← основний файл (зашифрований)
├── slot_1.sav.bak         ← резервна копія (старий стан)
├── slot_1.meta            ← метадані (без шифру, для UI)
├── slot_1.jpg             ← скріншот (опційно)
├── slot_1_snap_0.sav      ← rollback (найновіший)
├── slot_1_snap_1.sav
├── slot_1_snap_2.sav
├── autosave.sav
└── autosave.meta
```

---

## Часті помилки

**Збій MessagePack про відсутні `[MessagePackObject]` атрибути.**
Система використовує `ContractlessStandardResolver` — атрибути не обов'язкові для
кастомних класів. Але якщо ваш клас має складні generic-поля, які не серіалізуються
авто — додайте `[MessagePackObject(true)]`.

**`SaveId` дублюється — другий перезаписує перший.**
Перевірте Console на попередження від `SaveRegistry`. Виправте або через `[Saveable("unique_id")]`,
або через `override SaveId => "instance_" + name`.

**Mode 2: префаб не знайдено в реєстрі.**
Перевірте, що PrefabRegistry створено в проекті і прив'язано до SaveManager. У білді —
переконайтесь, що рантайм-інстансовані префаби мають компонент `PrefabSourceMarker`.

**Source Generator не запускається.**
Версія Unity має бути 2021.2+. DLL має мати label `RoslynAnalyzer` і знятими всіма
Platform-галочками. Перевірте Console на діагностику від генератора.

**Завантаження не відновлює стан.**
Об'єкт ISaveable не зареєстрований у `SaveRegistry`. Якщо успадковуєте `MonoBehaviour, ISaveable`
напряму (без `SaveableBehaviour` і без `[Saveable]`) — додайте `Register/Unregister` у
`OnEnable/OnDisable` вручну.
