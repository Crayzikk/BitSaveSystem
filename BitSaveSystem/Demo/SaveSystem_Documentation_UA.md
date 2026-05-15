# Документація системи збереження (Save System)

> Повна інструкція з використання та пояснення логіки кожного скрипта.

---

## Зміст

1. [Загальна архітектура](#1-загальна-архітектура)
2. [Як працює система — крок за кроком](#2-як-працює-система--крок-за-кроком)
3. [Два режими збереження: State і FullScene](#3-два-режими-збереження-state-і-fullscene)
4. [Атрибути: Saveable, Track, SaveIgnore](#4-атрибути-saveable-track-saveignore)
5. [ISaveable — контракт для всіх об'єктів](#5-isaveable--контракт-для-всіх-обєктів)
6. [SaveTable — таблиця даних](#6-savetable--таблиця-даних)
7. [Source Generator — авто-генерація OnSave / OnLoad](#7-source-generator--авто-генерація-onsave--onload)
8. [SaveableBehaviour — авто-реєстрація без генератора](#8-saveablebehaviour--авто-реєстрація-без-генератора)
9. [SaveRegistry — статичний реєстр об'єктів](#9-saveregistry--статичний-реєстр-обєктів)
10. [HashService — Dirty-перевірка через хешування](#10-hashservice--dirty-перевірка-через-хешування)
11. [SaveFile і SceneSnapshot — моделі файлу](#11-savefile-і-scenesnapshot--моделі-файлу)
12. [SaveSerializer — MessagePack + LZ4](#12-saveserializer--messagepack--lz4)
13. [EncryptionService — AES-256-CBC + HMAC-SHA256](#13-encryptionservice--aes-256-cbc--hmac-sha256)
14. [SceneCapture і SceneRestore — Mode 2](#14-scenecapture-і-scenerestore--mode-2)
15. [PrefabRegistry і PrefabSourceMarker — Сценарій A](#15-prefabregistry-і-prefabsourcemarker--сценарій-a)
16. [PrimitiveHelper і PrimitiveTypeMarker — Сценарій B](#16-primitivehelper-і-primitivetypemarker--сценарій-b)
17. [SaveablePrefabConverter — Editor-інструмент](#17-saveableprefabconverter--editor-інструмент)
18. [SceneObjectId — стабільний GUID для GameObject](#18-sceneobjectid--стабільний-guid-для-gameobject)
19. [MigrationManager — версійність і міграція](#19-migrationmanager--версійність-і-міграція)
20. [AutoSaveService — автозбереження](#20-autosaveservice--автозбереження)
21. [RollbackService — система відкату](#21-rollbackservice--система-відкату)
22. [ScreenshotService — скріншоти](#22-screenshotservice--скріншоти)
23. [SaveLog — діагностика](#23-savelog--діагностика)
24. [SaveManager — головний оркестратор](#24-savemanager--головний-оркестратор)
25. [Save Inspector Window — UI редактора](#25-save-inspector-window--ui-редактора)
26. [Покрокова інструкція: додати новий об'єкт](#26-покрокова-інструкція-додати-новий-обєкт)
27. [Покрокова інструкція: додати нову версію формату](#27-покрокова-інструкція-додати-нову-версію-формату)
28. [Покрокова інструкція: налаштувати Mode 2 у проекті](#28-покрокова-інструкція-налаштувати-mode-2-у-проекті)
29. [Повний потік даних: Save і Load](#29-повний-потік-даних-save-і-load)
30. [Обмеження системи](#30-обмеження-системи)
31. [Типові помилки та як їх уникнути](#31-типові-помилки-та-як-їх-уникнути)

---

## 1. Загальна архітектура

Система побудована за принципом **«оркестратор + незалежні сервіси»**. Кожен скрипт має одну чітко визначену відповідальність, і жоден із них не знає про деталі реалізації іншого.

```
┌────────────────────────────────────────────────────────────────┐
│                        SaveManager                             │  ← єдина точка входу
│  (Singleton, MonoBehaviour, оркестратор)                       │
└──┬──────┬──────────┬──────────┬──────────┬──────────┬──────────┘
   │      │          │          │          │          │
┌──▼──┐ ┌─▼────┐ ┌──▼────┐ ┌──▼────┐ ┌──▼───┐ ┌────▼──────┐
│Seri-│ │Encryp│ │Migra- │ │Roll-  │ │Auto- │ │ Scene     │
│aliz │ │tion  │ │tion   │ │back   │ │Save  │ │ Capture/  │
│MsgP+│ │AES-  │ │Manag- │ │Service│ │      │ │ Restore   │
│LZ4  │ │CBC + │ │er     │ │       │ │      │ │ (Mode 2)  │
│     │ │HMAC  │ │       │ │       │ │      │ │           │
└─────┘ └──────┘ └───────┘ └───────┘ └──────┘ └───────────┘

┌────────────────────────────────────────────────────────────────┐
│                       SaveRegistry                             │  ← статичний реєстр
│  HashSet<ISaveable> + Dictionary<SaveId, ISaveable>            │
└────────────────────────────────────────────────────────────────┘

   Об'єкти гри (само-реєстрація через OnEnable):
   ┌────────────────┐  ┌──────────────┐  ┌────────────────┐
   │  PlayerHealth  │  │  Inventory   │  │   QuestLog     │
   │  [Saveable]    │  │ SaveableBehv │  │  ручний        │
   │  [Track] поля  │  │  (без gener) │  │  ISaveable     │
   └────────────────┘  └──────────────┘  └────────────────┘
```

**Ключова ідея:** `SaveManager` не знає про жоден конкретний клас гри і не шукає об'єкти через `FindObjectsOfType`. Кожен об'єкт сам реєструється у `SaveRegistry` при увімкненні і видаляє себе при вимкненні. `SaveManager` просто читає реєстр.

**Дві шари даних:**
- **Логічний стан** (HP, gold, открыті двері) — через `ISaveable` і `SaveTable`. Працює в обох режимах.
- **Структура сцени** (які об'єкти існують, де вони стоять, які компоненти на них) — через `SceneCapture`/`SceneRestore`. Тільки в Mode 2.

---

## 2. Як працює система — крок за кроком

### При натисканні «Зберегти» (Mode 1)

1. Кодер викликає `SaveManager.Instance.Save("slot_1")`.
2. SaveManager запускає корутину `SaveCoroutine`. Корутина потрібна, бо скріншот вимагає `WaitForEndOfFrame`.
3. Замість `FindObjectsOfType` SaveManager читає `SaveRegistry.All` — миттєва O(1) операція.
4. Для кожного `ISaveable` обчислюється `ComputeStateHash()`. Якщо хеш збігається з попереднім — таблиця береться з попереднього файлу (лінива загрузка через `prevFile ??= ...`).
5. Для змінених — викликається `OnSave()`, що повертає `SaveTable`.
6. `SaveFile` серіалізується через MessagePack + LZ4.
7. Байти шифруються через AES-256-CBC + HMAC-SHA256.
8. Атомарний запис на диск: `.tmp` → `File.Replace` → `.sav` (старий стає `.bak`).
9. Окремо записується `.savmeta` — без шифрування, для UI.
10. Знімок додається до Rollback-черги.

### При натисканні «Зберегти всю сцену» (Mode 2)

Все те саме, плюс перед серіалізацією:

3a. `SceneCapture.Capture()` обходить усю сцену рекурсивно і для кожного `GameObject` вирішує:
- **Якщо це інстанс префаба** → зберігається `prefabGuid` + дельта змінених полів.
- **Якщо є PrimitiveTypeMarker** → зберігаються всі компоненти через рефлексію.
- **Інакше** → теж рефлексія, але з warning у Console (бо при Load меш не відновиться).

Результат складається у `SaveFile.Scene`, далі — той же шлях серіалізації.

### При завантаженні

Той самий шлях у зворотному порядку:
1. `File.ReadAllBytes` → байти.
2. `EncryptionService.Decrypt` → плейн-байти. Якщо HMAC не сходиться — fallback на `.bak`.
3. `SaveSerializer.Deserialize` → `SaveFile`.
4. `MigrationManager.Migrate` — якщо версія старіша.
5. `ApplyFile`:
   - Якщо це Mode 2 → `SceneRestore.Apply` реконструює сцену.
   - Для кожної таблиці шукаємо `ISaveable` у реєстрі за `SaveId` і викликаємо `OnLoad`.

---

## 3. Два режими збереження: State і FullScene

| Режим | API | Що зберігає | Коли використовувати |
|-------|-----|-------------|----------------------|
| **Mode 1: State** | `Save(slot)` | Тільки стан помічених `ISaveable` | Структура сцени фіксована, змінюються тільки атрибути об'єктів (HP, інвентар, прогрес квестів). |
| **Mode 2: FullScene** | `SaveFullScene(slot)` | Усі GameObjects сцени + їхні компоненти + стан | Динамічна сцена (вороги, трупи, підібрані предмети, відкриті скрині). |

**Mode 1 — швидко і компактно.** Файл ~1-10 КБ. Не пам'ятає, скільки ворогів у сцені — пам'ятає тільки HP гравця, gold, прапорці.

**Mode 2 — детально, але дорожче.** Файл може бути 50-500 КБ для типової сцени. Пам'ятає кожний GameObject, що існував на момент Save.

Обидва режими можна використовувати в одному проекті з різними slotId:
- `Save("autosave")` — швидке Mode 1 кожні 30 секунд.
- `SaveFullScene("checkpoint")` — Mode 2 на чек-поінтах.

---

## 4. Атрибути: Saveable, Track, SaveIgnore

Три атрибути для маркування класів і полів.

**`[Saveable]`** — позначає клас як такий, що його стан зберігається. Source Generator згенерує `OnSave`, `OnLoad`, `SaveId`, `ComputeStateHash` і реєстрацію.

```csharp
[Saveable("player_health")]           // ID = "player_health"
public partial class PlayerHealth : MonoBehaviour { ... }

[Saveable]                             // ID = "PlayerHealth" (ім'я класу)
public partial class PlayerHealth : MonoBehaviour { ... }

[Saveable(IncludeAllPublicFields = true)]
public partial class Inventory : MonoBehaviour
{
    public int Gold;        // включено
    public List<string> Items;  // включено
    [SaveIgnore] public float TempBuff;  // виключено
}
```

Клас має бути `partial`, бо Source Generator додає реалізацію інтерфейсу через `partial class`.

**`[Track]`** — позначає поле або властивість як таке, що його стан зберігається. Альтернатива `IncludeAllPublicFields`.

```csharp
[Saveable]
public partial class PlayerHealth : MonoBehaviour
{
    [Track("hp")]    public float CurrentHp;  // ключ у таблиці = "hp"
    [Track]          public bool  IsAlive;    // ключ = "IsAlive"
    public string DebugName;                  // НЕ зберігається (нема [Track])
}
```

**`[SaveIgnore]`** — виключає поле, навіть якщо клас має `IncludeAllPublicFields = true`.

---

## 5. ISaveable — контракт для всіх об'єктів

`ISaveable` — єдиний інтерфейс, що об'єднує систему. Будь-який клас, що зберігає стан, реалізує його.

```csharp
public interface ISaveable
{
    string SaveId { get; }              // стабільний ID між сесіями
    ulong  ComputeStateHash();           // FNV-1a хеш поточного стану
    SaveTable OnSave();                  // будує таблицю з даними
    void OnLoad(SaveTable table);        // відновлює стан з таблиці
}
```

**`SaveId`** — рядок, стабільний між сесіями. Не `GetInstanceID()`. Використовуйте константу (`"player_health"`) або `[Saveable("explicit_id")]`.

**`ComputeStateHash()`** — повертає FNV-1a 64-bit хеш від серіалізованих байтів усіх Track-полів. Якщо хеш не змінився між Save'ами, об'єкт пропускається (Dirty-skip). Source Generator генерує цей метод автоматично.

**`OnSave()`** — будує `SaveTable`. Source Generator генерує автоматично, або пишіть вручну.

**`OnLoad(SaveTable table)`** — відновлює стан. Якщо поле відсутнє — `table.Get<T>(key, default)` повертає значення за замовчуванням. Це забезпечує зворотну сумісність зі старими сейвами без міграції.

---

## 6. SaveTable — таблиця даних

`SaveTable` — структура «ключ → серіалізовані байти». Кожен `ISaveable` має свою таблицю. Всередині — `Dictionary<string, byte[]>`.

### Створення таблиці

```csharp
SaveTable.From(this);                    // ключ = ім'я класу
SaveTable.From(this, "left_door");       // ключ = "ClassName.left_door"
SaveTable.From("global_settings");       // довільний рядок
```

### Запис через fluent API

```csharp
return SaveTable.From(this)
    .Set("hp", currentHp)            // float
    .Set("items", items)             // List<string>
    .SetVector3("pos", transform.position)
    .SetQuaternion("rot", transform.rotation)
    .SetColor("color", renderer.material.color);
```

`Set<T>` підтримує будь-який MessagePack-сумісний тип: примітиви, рядки, масиви, `List<T>`, `Dictionary<K,V>`, кастомні структури.

Для Unity-типів (`Vector3`, `Quaternion`, `Color`) є окремі методи — вони серіалізують як `float[]`, що компактніше за маркування `[MessagePackObject]` на Unity-структурах.

### Читання з дефолтом

```csharp
hp = table.Get<float>("hp", 100f);
items = table.Get<List<string>>("items", new List<string>());
position = table.GetVector3("pos", Vector3.zero);
```

Якщо ключа немає або тип не збігається — повертається default. Жодних винятків.

---

## 7. Source Generator — авто-генерація OnSave / OnLoad

Source Generator — компонент компіляції Roslyn, що автоматично створює реалізацію `ISaveable` для класів з `[Saveable]`.

### Дві форми використання

**Форма 1: повний пакет**

```csharp
[Saveable("player_health")]
public partial class PlayerHealth : MonoBehaviour
{
    [Track("hp")]    public float CurrentHp = 100f;
    [Track]          public bool  IsAlive   = true;
    [SaveIgnore]     public float TempBuff;
}
```

Згенерує:
- `string SaveId => "player_health";`
- `ulong ComputeStateHash()` — FNV-1a по всіх Track-полях
- `SaveTable OnSave()` — пакує всі поля
- `void OnLoad(SaveTable t)` — розпаковує
- `private void OnEnable() => SaveRegistry.Register(this);`
- `private void OnDisable() => SaveRegistry.Unregister(this);`

Кодеру нічого не треба писати — навіть реєстрації.

**Форма 2: тільки helper-методи**

```csharp
public partial class CustomLogic : MonoBehaviour, ISaveable
{
    [Track("score")] private int _score;

    public string SaveId => "custom_logic";
    public ulong ComputeStateHash() => ComputeFieldsHash();   // згенеровано
    public SaveTable OnSave()
    {
        var t = SaveTable.From(this);
        WriteFieldsTo(t);                                       // згенеровано
        return t;
    }
    public void OnLoad(SaveTable t) => ReadFieldsFrom(t);       // згенеровано

    private void OnEnable()  => SaveRegistry.Register(this);
    private void OnDisable() => SaveRegistry.Unregister(this);
}
```

Тут генератор додає тільки три helper-методи (`ComputeFieldsHash`, `WriteFieldsTo`, `ReadFieldsFrom`), а основну логіку пише кодер.

### Як встановити генератор

Source Generator — окрема DLL, скомпільована з .NET Standard 2.0. Розташування: `Assets/BitSaveSystem/SourceGenerator/SaveSystem.SourceGenerator.dll`. На DLL має стояти мітка `RoslynAnalyzer` і зняті всі Platform-галочки.

Якщо генератор не встановлено — клас з `[Saveable]` не скомпілюється. Альтернатива — `SaveableBehaviour` (див. наступну секцію).

### Вимоги

- Unity 2021.2+
- DLL з міткою `RoslynAnalyzer`
- Клас має бути `partial`

---

## 8. SaveableBehaviour — авто-реєстрація без генератора

Якщо Source Generator не встановлено або вам потрібний повний контроль над OnSave/OnLoad — успадковуйте `SaveableBehaviour`.

```csharp
public class Inventory : SaveableBehaviour
{
    [SerializeField] private int _gold;
    [SerializeField] private List<string> _items = new();

    public override string SaveId => "inventory";

    public override SaveTable OnSave() => SaveTable.From(this)
        .Set("gold", _gold)
        .Set("items", _items);

    public override void OnLoad(SaveTable t)
    {
        _gold  = t.Get<int>("gold", 0);
        _items = t.Get<List<string>>("items", new List<string>());
    }

    // ComputeStateHash() — fallback через OnSave + хеш від серіалізованої таблиці.
    // Повільніше за генератор, але працює без атрибутів.
}
```

`SaveableBehaviour.OnEnable/OnDisable` автоматично реєструють/видаляють об'єкт у `SaveRegistry`. Якщо потрібно перевизначити — викликайте `base.OnEnable()`.

---

## 9. SaveRegistry — статичний реєстр об'єктів

`SaveRegistry` замінив `FindObjectsOfType`. Статичний клас з `HashSet<ISaveable>` + `Dictionary<string, ISaveable>` для пошуку за SaveId.

**Чому HashSet:** Register/Unregister — O(1). Дублікати ігноруються автоматично.

**Чому статичний:** реєстр має існувати незалежно від будь-якого GameObject — це глобальний стан системи.

### Публічне API

```csharp
SaveRegistry.All                            // IReadOnlyCollection<ISaveable>
SaveRegistry.Count                          // кількість зареєстрованих
SaveRegistry.Find("player_health")          // ISaveable або null
SaveRegistry.AnyDirty(prevHashes)           // чи змінився хоч один з моменту prevHashes
SaveRegistry.Clear()                        // очистити при зміні сцени
SaveRegistry.Register(saveable)
SaveRegistry.Unregister(saveable)
```

### Очищення при зміні сцени

`SaveManager` підписаний на `SceneManager.sceneLoaded`. При завантаженні в режимі `Single` реєстр очищається, об'єкти нової сцени реєструються самі. При `Additive` — реєстр не чіпається (нові об'єкти просто додаються).

### Виявлення дублікатів

При реєстрації другого об'єкта з тим же `SaveId` у Console виводиться warning:

```
[SaveSystem] Duplicate SaveId 'player_health' (PlayerHealth vs PlayerHealth).
Second overrides first.
```

Другий об'єкт перезапише запис першого в Dictionary, але обидва залишаться в HashSet.

---

## 10. HashService — Dirty-перевірка через хешування

Замість прапорця `IsDirty` і обгорток `Tracked<T>`, що були у старій системі, тепер використовується FNV-1a 64-bit хеш від серіалізованих байтів.

### Як це працює

При кожному Save для кожного `ISaveable` обчислюється `ComputeStateHash()`:

```csharp
ulong hash = HashService.Fnv1a(MessagePackSerializer.Serialize(allTrackFields));
```

Хеш записується у `SaveFile.Hashes` (Dictionary<saveId, hash>). При наступному Save:

```csharp
foreach (var sv in SaveRegistry.All)
{
    ulong currentHash = sv.ComputeStateHash();
    if (previousHashes.TryGetValue(sv.SaveId, out var prevHash) && prevHash == currentHash)
    {
        // Об'єкт не змінився — беремо таблицю з попереднього файлу
        file.Tables[sv.SaveId] = prevFile.Tables[sv.SaveId];
        continue;
    }
    // Змінився — викликаємо OnSave
    file.Tables[sv.SaveId] = sv.OnSave();
}
```

### Переваги перед Tracked<T>

- **Працює з колекціями автоматично.** `list.Add()` змінює хеш списку — система бачить зміну.
- **Не потрібно загортати поля.** Просто `[Track] public int Hp;` замість `Tracked<int> Hp`.
- **Не можна забути виставити прапорець.** Стан і хеш завжди синхронізовані.
- **Складніший** — обчислення хешу повільніше за читання прапорця. Але різниця в мікросекундах.

---

## 11. SaveFile і SceneSnapshot — моделі файлу

`SaveFile` — кореневий об'єкт, що серіалізується цілком і записується на диск.

```csharp
[MessagePackObject]
public sealed class SaveFile
{
    [Key(0)] public int    Version;
    [Key(1)] public long   TimestampUtc;
    [Key(2)] public string SceneName;
    [Key(3)] public string SlotId;
    [Key(4)] public SaveMode Mode;        // State або FullScene
    [Key(5)] public Dictionary<string, SaveTable> Tables;   // Mode 1
    [Key(6)] public SceneSnapshot Scene;  // Mode 2
    [Key(7)] public Dictionary<string, ulong> Hashes;       // для Dirty-skip
}
```

**Tables** заповнюється в обох режимах — для всіх ISaveable у сцені.

**Scene** заповнюється тільки в Mode 2 — містить повну структуру всіх GameObject.

**SceneSnapshot** містить список `SceneObject`:

```csharp
public sealed class SceneObject
{
    public string ObjectGuid;            // з SceneObjectId
    public string Name;
    public bool ActiveSelf;
    public string ParentGuid;
    public int Layer;
    public string Tag;
    public float[] LocalPosition;        // [x, y, z]
    public float[] LocalRotation;        // [x, y, z, w]
    public float[] LocalScale;

    public string PrefabGuid;            // якщо інстанс префаба
    public List<ComponentDelta> ComponentDeltas;  // дельта від префаба

    public List<ComponentSnapshot> Components;    // fallback без префаба
    public List<SaveTable> SaveableTables;        // від ISaveable на цьому GO
}
```

**SlotMeta** — окремий файл `.savmeta` без шифрування, для швидкого читання в UI меню вибору слота (час збереження, scene name, наявність скріншота).

---

## 12. SaveSerializer — MessagePack + LZ4

Відповідає за перетворення `SaveFile` у байти і назад. Один виклик MessagePack з опцією `Lz4BlockArray` робить обидва кроки.

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithCompression(MessagePackCompression.Lz4BlockArray)
    .WithResolver(ContractlessStandardResolver.Instance);

byte[] bytes = MessagePackSerializer.Serialize(file, options);
SaveFile file = MessagePackSerializer.Deserialize<SaveFile>(bytes, options);
```

**ContractlessStandardResolver** — дозволяє серіалізувати кастомні класи без `[MessagePackObject]`. Зручно для прототипу, повільніше за класи з атрибутами. Для прод-коду додавайте `[MessagePackObject(true)]` на ваші структури.

**Lz4BlockArray** — стиснення LZ4 з блочною структурою. У 5-10 разів швидше за GZip при розпакуванні. Для типового сейву ~3-5x компактніше за нестислий MessagePack.

Приблизні розміри для save з 10 ISaveable:
- JSON без стиснення: ~4000 байт
- MessagePack без LZ4: ~1200 байт
- MessagePack + LZ4: ~600 байт

### ToDebugJson

```csharp
string json = serializer.ToDebugJson(bytes);
```

Конвертує MessagePack-байти у читабельний JSON. Використовується тільки в Save Inspector Window для превʼю даних.

---

## 13. EncryptionService — AES-256-CBC + HMAC-SHA256

### Чому не AES-GCM

`AesGcm` кидає `PlatformNotSupportedException` на Unity до 2021.2 і деяких мобільних таргетах. `Aes.Create()` (CBC режим) працює **скрізь** без винятків.

### Чому CBC + HMAC

AES-CBC не перевіряє цілісність — якщо хтось змінить байти, CBC «розшифрує» і поверне сміття без помилки. Тому використовується **Encrypt-then-MAC**:

1. Шифруємо дані (CBC).
2. Підписуємо зашифрований результат HMAC-SHA256 з окремим ключем.
3. При розшифровці спершу перевіряємо HMAC. Якщо не сходиться — `CryptographicException`, SaveManager переключається на `.bak`.

### Структура зашифрованого файлу

```
[hmac: 32 байти][iv: 16 байт][ciphertext: N байт]
```

IV генерується криптографічно безпечним генератором при кожному шифруванні — однакові дані щоразу шифруються по-різному.

### ConstantTimeEquals

Порівняння HMAC виконується **за фіксований час** (XOR усіх байтів) для захисту від timing attacks. Не використовуйте `==` для перевірки криптографічних значень.

### Два ключі у PlayerPrefs

Зберігаються під `SS_EncKey_v3` (AES) і `SS_MacKey_v3` (HMAC). Суфікс `_v3` відрізняє їх від попередніх версій.

### Кастомні ключі

```csharp
byte[] encKey = ...; // 32 байти
byte[] macKey = ...; // 32 байти
var service = new EncryptionService(encKey, macKey);
```

Корисно для cloud save: однакові ключі на всіх пристроях користувача (наприклад, виведені з акаунту).

---

## 14. SceneCapture і SceneRestore — Mode 2

Найбільший і найскладніший компонент системи. Захоплює і відновлює повну структуру сцени.

### SceneCapture.Capture()

Обходить сцену рекурсивно через `Scene.GetRootGameObjects()`. Для кожного GameObject:

1. Гарантує наявність `SceneObjectId` (додає якщо немає).
2. Заповнює базові дані: name, active, parent, layer, tag, transform.
3. Збирає `SaveTable` від усіх `ISaveable`-компонентів.
4. **Стратегія збереження компонентів:**
   - **Якщо є префаб (через `PrefabUtility.GetCorrespondingObjectFromOriginalSource` або `PrefabSourceMarker`)** → зберігаємо `PrefabGuid` + дельту полів через `CaptureDeltas()`.
   - **Інакше** → серіалізуємо всі компоненти через рефлексію (`CaptureAllComponents`).
   - Якщо обджект має `Renderer`, але немає префаба і `PrimitiveTypeMarker` — `WarnIfNotRestorable()` видає warning.

### SceneRestore.Apply()

1. Збирає мапу існуючих GameObject у сцені за `SceneObjectId.Guid`.
2. Для кожного `SceneObject` зі snapshot:
   - **Якщо існує в сцені** → оновлюємо стан.
   - **Якщо є PrefabGuid** → `Instantiate(prefab)` + застосовуємо дельту.
   - **Якщо немає, але є PrimitiveTypeMarker серед збережених компонентів** → `PrimitiveHelper.CreatePrimitive(SavedType, SavedColor)`. Унікальна логіка — викликає `GameObject.CreatePrimitive`, який сам додає `MeshFilter`+`MeshRenderer`+`Collider` з посиланнями на вбудовані Unity-asset'и.
   - **Інакше** → `new GameObject(name)` + `AddComponent` для кожного збереженого компонента (без меша/матеріалу).
3. Двопрохідно встановлюємо parent (другим проходом, коли всі об'єкти створено).
4. Викликаємо `OnLoad` для всіх `ISaveable` на GO.
5. Якщо `destroyMissing=true` — видаляємо з сцени GO, яких немає в snapshot.

### ComponentReflector

Допоміжний клас, що читає і пише поля Unity-компонентів через рефлексію. Правила:

- Тільки публічні поля та поля з `[SerializeField]`.
- Без `[SaveIgnore]` і `[NonSerialized]`.
- Підтримувані типи: примітиви, рядки, enum, `Vector2/3/4`, `Quaternion`, `Color`, `Rect`, `Bounds`, масиви, `List<T>`.
- **Не серіалізуються `UnityEngine.Object` посилання** (Material, Mesh, Texture, AudioClip) — це обмеження за дизайном.

Кеш `Dictionary<Type, FieldInfo[]>` гарантує, що рефлексія виконується один раз на тип.

---

## 15. PrefabRegistry і PrefabSourceMarker — Сценарій A

**PrefabRegistry** — ScriptableObject з мапою `prefabGuid → GameObject`. Створюється:

- Автоматично при першому виклику `SaveablePrefabConverter` (Editor-інструмент).
- Вручну через `Assets → Create → SaveSystem → Prefab Registry`.

`PrefabRegistryPostprocessor` (Editor-only) автоматично додає кожен імпортований префаб до реєстру, якщо реєстр існує.

**PrefabSourceMarker** — компонент, що ставиться на префаб і зберігає його `PrefabGuid`. Потрібен для білдів: у білді `PrefabUtility.GetCorrespondingObjectFromOriginalSource` недоступний, тому SceneCapture читає `PrefabGuid` із цього маркера.

Інструмент `Convert to Saveable Prefab` додає `PrefabSourceMarker` автоматично.

### Як працює Save через префаб

```
SceneCapture бачить GO
    → знаходить prefab через PrefabUtility (Editor) або PrefabSourceMarker (Build)
    → знаходить PrefabGuid у PrefabRegistry
    → ComponentReflector.CaptureDelta(instanceComp, prefabComp) для кожного компонента
    → зберігає тільки поля, що відрізняються
```

Розмір на один обджект: 50-200 байт (тільки дельта). Якщо без префаба — 500-2000 байт.

---

## 16. PrimitiveHelper і PrimitiveTypeMarker — Сценарій B

Для рантайм-примітивів (Cube, Sphere, Capsule, тощо), які ви створюєте з коду без префаба.

```csharp
// Замість цього:
var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
go.GetComponent<Renderer>().material.color = Color.red;
// (після Save Full Scene + Load — буде "голий" GameObject без меша)

// Зробіть так:
var go = PrimitiveHelper.CreatePrimitive(PrimitiveType.Cube, Color.red);
// (PrimitiveTypeMarker додано автоматично — Mode 2 коректно відновить)
```

**PrimitiveTypeMarker** зберігає два поля:
- `PrimitiveType SavedType` (enum)
- `Color SavedColor` (через `SetColor` → float[4])

Обидва — примітивні значення, що серіалізуються рефлексією без проблем.

При Load `SceneRestore` бачить серед збережених компонентів `PrimitiveTypeMarker`, читає `SavedType`, викликає `GameObject.CreatePrimitive(SavedType)`. Unity сам додає `MeshFilter`+`MeshRenderer`+`Collider` з вбудованих asset'ів (`Cube.fbx`, `Default-Material`). Потім `PrimitiveHelper.CreatePrimitive` додає сам маркер. Колір накладається з `SavedColor`.

### Обмеження

Підтримуються тільки стандартні Unity-примітиви. Складніші матеріали (текстура, шейдер, металічність, normal map) — не зберігаються. Для них використовуйте Сценарій A.

---

## 17. SaveablePrefabConverter — Editor-інструмент

Editor-only утиліта для одного клікa перетворення сценного об'єкта на saveable-префаб.

**Доступ:** правий клік на GameObject у Hierarchy → **SaveSystem → Convert to Saveable Prefab**.

**Що робить:**

1. Гарантує наявність `PrefabRegistry` у проекті (створює `Assets/SaveablePrefabs/PrefabRegistry.asset` якщо нема).
2. Гарантує наявність папки `Assets/SaveablePrefabs/`.
3. Викликає `PrefabUtility.SaveAsPrefabAssetAndConnect()` — створює префаб і перетворює сценний інстанс на інстанс префаба.
4. Додає `PrefabSourceMarker` на сам префаб з його GUID.
5. Реєструє префаб у `PrefabRegistry`.
6. Якщо в сцені є `SaveManager` з порожнім полем `Prefab Registry` — автоматично прив'язує.

Після цього об'єкт готовий до Mode 2 збереження за Сценарієм A.

---

## 18. SceneObjectId — стабільний GUID для GameObject

Компонент, що зберігає GUID кожного GameObject. Потрібен для Mode 2, щоб після Load знайти "той самий" об'єкт у сцені, що був на момент Save.

```csharp
[DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
public sealed class SceneObjectId : MonoBehaviour
{
    [SerializeField] private string _guid;
    public string Guid => _guid;
}
```

- У Editor `OnValidate()` автоматично призначає GUID для сценних об'єктів (не для префабів-asset'ів).
- При `Instantiate(prefab)` в рантаймі — `Guid` геттер створить новий GUID лениво.
- При SceneCapture/Restore — використовується як ключ для зіставлення snapshot ↔ scene.

### Чому DefaultExecutionOrder = -10000

Щоб GUID був ініціалізований до того, як будь-який інший Awake звернеться до нього.

---

## 19. MigrationManager — версійність і міграція

При завантаженні `SaveManager` викликає `MigrationManager.Migrate(file, currentVersion)`. Якщо `file.Version < currentVersion` — запускається ланцюжок міграторів.

```csharp
public interface IMigrator
{
    int FromVersion { get; }
    int ToVersion   { get; }
    SaveFile Migrate(SaveFile file);
}
```

Реєстрація в Bootstrap:

```csharp
MigrationManager.Register(new MigratorV1toV2());
MigrationManager.Register(new MigratorV2toV3());
```

При завантаженні v1 і currentVersion=3 — пройде v1→v2→v3 автоматично.

Якщо немає мігратора для якогось кроку — у Console з'явиться error, але завантаження не падає (просто дані з відсутніх полів буде взято з default).

---

## 20. AutoSaveService — автозбереження

Таймер, що періодично викликає Save через делегат, переданий з `SaveManager`.

### Режими

- **Interval** — кожні N секунд (5-3600, в `AutoSaveConfig.IntervalSeconds`).
- **OnTrigger** — тільки при `TriggerAutoSave()`. Для checkpoint-системи.
- **Hybrid** — обидва.
- **Disabled** — тільки ручне збереження.

### Dirty-перевірка

Якщо `SkipIfNothingChanged = true` — перед збереженням викликається `SaveRegistry.AnyDirty(previousHashes)`. Якщо жоден ISaveable не змінився — Save не виконується.

### AutoSaveConfig

ScriptableObject. Створіть через `Assets → Create → SaveSystem → AutoSave Config`. Прив'яжіть до поля `Auto Save Config` на SaveManager в Inspector.

Або програмно:

```csharp
var cfg = ScriptableObject.CreateInstance<AutoSaveConfig>();
cfg.Mode = AutoSaveMode.Interval;
cfg.IntervalSeconds = 60f;
cfg.SaveMode = SaveMode.State;       // або FullScene
cfg.SlotId = "autosave";
cfg.SkipIfNothingChanged = true;
SaveManager.Instance.StartAutoSave(cfg);
```

---

## 21. RollbackService — система відкату

Зберігає чергу останніх N знімків для кожного слота. Після переповнення найстаріший видаляється і з пам'яті, і з диска.

Файли: `{slotId}_snap_0.sav`, `{slotId}_snap_1.sav`, ..., `{slotId}_snap_{N-1}.sav`. Зберігаються вже зашифрованими.

```csharp
SaveManager.Instance.Rollback("slot_1", stepsBack: 1);  // на 1 збереження назад
SaveManager.Instance.Rollback("slot_1", stepsBack: 3);  // на 3 назад
```

Якщо запит на більше кроків, ніж є знімків — повертається найстаріший доступний.

Працює з обома режимами (Mode 1 і Mode 2), але **кожен slotId має власну чергу**. `Rollback("game")` не зачепить `Rollback("game_scene")`.

---

## 22. ScreenshotService — скріншоти

Корутина, що:

1. `yield return new WaitForEndOfFrame()` — обов'язково чекаємо кінця кадру.
2. `Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture()`.
3. (Опційно) Resize до `maxWidth` через `Graphics.Blit` + `RenderTexture`.
4. `tex.EncodeToJPG(quality)` — за замовчуванням quality=60, ~30-80 КБ.
5. `File.WriteAllBytes` поруч із основним сейвом.
6. `Object.Destroy(tex)` — звільняємо текстуру.

Виклик — через параметри `Save`:

```csharp
SaveManager.Instance.Save("slot_1", captureScreenshot: true);
// → slot_1.jpg

SaveManager.Instance.Save("slot_1", captureScreenshot: true, screenshotName: "boss_fight");
// → boss_fight.jpg
```

Кодер задає `captureScreenshot: true` — інакше скріншот не робиться (за замовчуванням `false`).

---

## 23. SaveLog — діагностика

Кільцевий буфер останніх N операцій (за замовчуванням 500). Кожен запис містить:

```csharp
public class SaveLogEntry
{
    public DateTime Timestamp;
    public SaveOperationKind Operation;   // Save, Load, AutoSave, Rollback, ...
    public string SlotId;
    public SaveMode Mode;
    public string FilePath;
    public long FileSizeBytes;
    public string CallSite;               // "Class.Method (file:line)"
    public string Stack;                  // короткий стек
    public bool Encrypted, Compressed;
    public int Version;
    public string DataPreview;            // JSON для Editor
    public string Note;
    public bool Success;
    public string Error;
    public double DurationMs;
}
```

CallSite береться через `StackTrace` з пропуском внутрішніх кадрів SaveManager. Це дозволяє в UI бачити, з якого скрипта і рядка викликали Save.

`SaveLog.OnEntryAdded` — подія, на яку підписаний Save Inspector Window для оновлення UI у реальному часі.

---

## 24. SaveManager — головний оркестратор

Singleton MonoBehaviour, що тримає всі сервіси і надає публічне API.

### Інспектор

| Поле | Опис |
|------|------|
| Custom Save Path | Куди писати. Якщо порожнє — Application.persistentDataPath. |
| Use Assets Folder In Editor | В Editor пише в `Assets/SaveData~/`. Тильда означає, що Unity ігнорує папку. |
| Encryption Enabled | AES-256-CBC + HMAC. |
| Compression Enabled | LZ4. |
| Rollback Enabled | Створювати snap-файли. |
| Max Snapshots | Розмір rollback-черги. |
| Current Version | Версія формату для міграції. |
| Prefab Registry | Для Mode 2. |
| Auto Save Config | Для автозбереження. |

Усе доступне програмно через властивості `Instance.EncryptionEnabled`, тощо.

### Публічне API

```csharp
SaveManager.Instance.Save(slot, screenshot, screenshotName);
SaveManager.Instance.SaveFullScene(slot, screenshot, screenshotName);
SaveManager.Instance.Load(slot);
SaveManager.Instance.Rollback(slot, stepsBack);
SaveManager.Instance.StartAutoSave(config);
SaveManager.Instance.TriggerAutoSave();
SaveManager.Instance.SlotPath(slot);
SaveManager.Instance.MetaPath(slot);
SaveManager.Instance.SaveDir;
```

### Атомарний запис

```csharp
private static void WriteAtomic(string path, byte[] data)
{
    string tmp = path + ".tmp";
    string bak = path + ".bak";
    File.WriteAllBytes(tmp, data);
    if (File.Exists(path))
        File.Replace(tmp, path, bak);  // atomic: tmp→main, main→bak
    else
        File.Move(tmp, path);          // перший Save — File.Replace вимагає destination
}
```

### Лінива загрузка попереднього файлу

При Save з кешем хешів об'єкти, що не змінились, беруть свою таблицю з попереднього файлу. Файл читається максимум один раз:

```csharp
prevFile = TryReadExisting(slotId);  // null якщо нема або зламано
```

### Файлова структура на диску

```
{SaveDir}/
├── slot_1.sav           ← основний (зашифрований)
├── slot_1.sav.bak       ← бекап
├── slot_1.savmeta       ← метадані (не зашифровано)
├── slot_1.jpg           ← скріншот
├── slot_1_snap_0.sav    ← rollback (найновіший)
├── slot_1_snap_1.sav
├── slot_1_snap_2.sav
├── autosave.sav
└── autosave.savmeta
```

`.savmeta` (а не `.meta`) — щоб не конфліктувати з форматом asset-meta-файлів Unity.

`Assets/SaveData~/` (з тильдою) — щоб Unity ігнорувала папку (не імпортувала файли як asset'и).

---

## 25. Save Inspector Window — UI редактора

Editor-only вікно: **Window → Save System → Inspector**.

Показує:

- **Список файлів** у папці збереження: ім'я, розмір, час останньої зміни. Кнопка Reveal відкриває папку у провіднику.
- **Журнал операцій**: всі Save, Load, AutoSave, Rollback, ScreenshotCapture, Encryption, Decryption, Compression, Migration.
- **Деталі вибраного запису справа**: час, операція, slot, mode, файл, розмір, чи зашифровано/стиснено, версія, **call-site** (звідки викликано), стек, тривалість, **JSON-превʼю** даних.

**Фільтри:**
- Search — пошук по тексту в slot, callsite, file, note.
- Op — фільтр за типом операції.
- Slot — фільтр за конкретним слотом.
- Mode — фільтр за State / FullScene.

---

## 26. Покрокова інструкція: додати новий об'єкт

### Варіант A: через Source Generator (рекомендовано)

**Крок 1.** Зробіть клас `partial` і додайте `[Saveable]`:

```csharp
[Saveable("quest_tracker")]
public partial class QuestTracker : MonoBehaviour
{
    [Track]
    public List<string> CompletedQuests = new();

    [Track]
    public int Chapter = 1;
}
```

**Крок 2.** Готово. Source Generator згенерує `OnSave`, `OnLoad`, `SaveId`, `ComputeStateHash`, `OnEnable/OnDisable` з реєстрацією.

### Варіант B: через SaveableBehaviour (без генератора)

**Крок 1.** Успадкуйте `SaveableBehaviour`:

```csharp
public class QuestTracker : SaveableBehaviour
{
    private List<string> _quests = new();
    private int _chapter = 1;

    public override string SaveId => "quest_tracker";

    public override SaveTable OnSave() => SaveTable.From(this)
        .Set("quests", _quests)
        .Set("chapter", _chapter);

    public override void OnLoad(SaveTable t)
    {
        _quests = t.Get<List<string>>("quests", new List<string>());
        _chapter = t.Get<int>("chapter", 1);
    }
}
```

Реєстрація — автоматично через `SaveableBehaviour.OnEnable`.

### Варіант C: ручна реалізація ISaveable

Якщо не можна успадковувати `SaveableBehaviour`:

```csharp
public class QuestTracker : MonoBehaviour, ISaveable
{
    public string SaveId => "quest_tracker";
    public ulong ComputeStateHash() => HashService.HashSaveTable(OnSave());
    public SaveTable OnSave() { ... }
    public void OnLoad(SaveTable t) { ... }

    private void OnEnable()  => SaveRegistry.Register(this);
    private void OnDisable() => SaveRegistry.Unregister(this);
}
```

---

## 27. Покрокова інструкція: додати нову версію формату

**Крок 1.** Підніміть `SaveManager.CurrentVersion` (наприклад, з 2 до 3).

**Крок 2.** Напишіть мігратор:

```csharp
public class MigratorV2toV3 : IMigrator
{
    public int FromVersion => 2;
    public int ToVersion   => 3;

    public SaveFile Migrate(SaveFile file)
    {
        if (file.Tables.TryGetValue("PlayerStats", out var table))
            if (!table.Has("stamina"))
                table.Set("stamina", 100f);
        return file;
    }
}
```

**Крок 3.** Зареєструйте мігратор десь у Bootstrap:

```csharp
public class Bootstrap : MonoBehaviour
{
    private void Awake()
    {
        MigrationManager.Register(new MigratorV1toV2());
        MigrationManager.Register(new MigratorV2toV3());  // ← додано
    }
}
```

Готово. Старі сейви автоматично пройдуть v1→v2→v3 при наступному завантаженні.

---

## 28. Покрокова інструкція: налаштувати Mode 2 у проекті

**Крок 1.** Створіть `PrefabRegistry` через `Assets → Create → SaveSystem → Prefab Registry`. Або просто натисніть на будь-якому об'єкті в Hierarchy `Convert to Saveable Prefab` — реєстр створиться автоматично.

**Крок 2.** Прив'яжіть `PrefabRegistry` до поля `Prefab Registry` на компоненті `SaveManager`. Інструмент Convert робить це автоматично, якщо поле порожнє.

**Крок 3.** Для всіх префабів, які можуть з'являтись у сцені динамічно (вороги, лут, projectiles) — переконайтеся, що:
- Префаб імпортовано (PrefabRegistryPostprocessor сам додасть до реєстру).
- На префабі є компонент `PrefabSourceMarker` (Convert додає; для існуючих — додайте вручну).

**Крок 4.** Для рантайм-примітивів використовуйте `PrimitiveHelper.CreatePrimitive` замість `GameObject.CreatePrimitive`.

**Крок 5.** Викликайте `SaveManager.Instance.SaveFullScene("slot")` і `Load("slot")`.

---

## 29. Повний потік даних: Save і Load

### Save (Mode 1)

```
Save("slot_1")
    └─► SaveCoroutine()
            ├─► (опційно) WaitForEndOfFrame → CaptureScreenshot()
            ├─► SaveRegistry.All
            ├─► для кожного ISaveable:
            │       ├─► hash = sv.ComputeStateHash()
            │       ├─► якщо hash == prevHashes[sv.SaveId] → таблиця з prevFile (лінива)
            │       └─► інакше → sv.OnSave() → SaveTable
            ├─► new SaveFile { Tables, Hashes, Mode = State }
            ├─► SaveSerializer.Serialize(file)        → byte[]  (MessagePack + LZ4)
            ├─► EncryptionService.Encrypt(bytes)      → byte[]  (AES + HMAC)
            ├─► WriteAtomic(path, bytes)
            │       ├─► перший раз: .tmp → File.Move  → .sav
            │       └─► повторно:    .tmp → File.Replace → .sav  (+ .bak)
            ├─► File.WriteAllBytes(MetaPath, meta)    → .savmeta (не зашифровано)
            └─► RollbackService.Push(slotId, file)    → _snap_N.sav
```

### Save (Mode 2)

Те саме плюс перед `Serialize`:

```
            ├─► SceneCapture.Capture(prefabRegistry):
            │       └─► для кожного GameObject у сцені:
            │               ├─► SceneObjectId — гарантуємо наявність
            │               ├─► transform, name, active, parent, layer, tag
            │               ├─► від ISaveable-компонентів → SaveTable
            │               └─► стратегія компонентів:
            │                       ├─► префаб → PrefabGuid + ComponentDeltas
            │                       └─► без префаба → ComponentSnapshot для кожного
            │                                          + warn якщо є Renderer без маркера
            └─► file.Scene = snapshot
```

### Load

```
Load("slot_1")
    └─► LoadInternal()
            ├─► File.ReadAllBytes(SlotPath)
            ├─► EncryptionService.Decrypt(bytes)       → byte[]
            │       └─► CryptographicException → читаємо .bak → ще раз Decrypt
            ├─► SaveSerializer.Deserialize(bytes)      → SaveFile
            ├─► MigrationManager.Migrate(file, current) → можливо оновлено
            └─► ApplyFile(file):
                    ├─► якщо Mode == FullScene → SceneRestore.Apply(file.Scene, registry)
                    │       ├─► створення/оновлення GO (префаб / маркер / new GO)
                    │       ├─► transform, active, layer, tag
                    │       ├─► parent-child (другим проходом)
                    │       ├─► OnLoad для ISaveable на GO
                    │       └─► destroyMissing — видалити GO, яких нема в snapshot
                    └─► для кожного table в file.Tables:
                            └─► SaveRegistry.Find(saveId).OnLoad(table)
```

---

## 30. Обмеження системи

### Mode 2

**Серіалізація мешів і матеріалів.** Система НЕ серіалізує `Mesh`, `Material`, `Texture`, `AudioClip` та інші `UnityEngine.Object` посилання. Вони відновлюються тільки через префаб (Сценарій A) або `PrimitiveTypeMarker` (Сценарій B).

**Material.color на runtime.** `renderer.material.color = ...` створює instance-копію матеріалу, що губиться при Stop Play. Для runtime-кольорів використовуйте `PrimitiveHelper`.

**Об'єкти DontDestroyOnLoad** у Mode 2 захоплюються тільки якщо знаходяться в активній сцені.

### Mode 1

**Тільки помічені ISaveable.** Якщо забули зареєструвати компонент — стан не збережеться.

**Порядок Save/Load між об'єктами не гарантований.** Якщо A залежить від B при OnLoad — використовуйте `SaveRegistry.Find` всередині OnLoad для лінивого зчитування.

### Перенос сейвів між пристроями

**HMAC ключ per-machine.** За замовчуванням EncryptionService генерує ключі в PlayerPrefs цього комп'ютера. Сейв з одного ПК на іншому не розшифрується.

Для cloud save: `new EncryptionService(encKey, macKey)` з однаковими ключами на всіх пристроях.

### Размір і продуктивність

**Mode 2 з 1000+ обджектів:** Save 100-500 мс, файл сотні КБ.

**Rollback з Mode 2:** кожен snap — повний знімок сцени. 5 snap × 500 КБ = 2.5 МБ на слот.

**Dirty-skip не оптимізує Mode 2.** Сцена завжди захоплюється повністю.

### Технічні обмеження

**Source Generator потребує Unity 2021.2+.** На старіших — тільки `SaveableBehaviour`.

**IL2CPP stripping.** Виставте `Managed Stripping Level = Minimal` або додайте `link.xml`.

**WebGL.** `File.WriteAllBytes` працює, але сейви живуть тільки в IndexedDB браузера.

**Multi-scene.** Захоплюється тільки `GetActiveScene()`. Інші завантажені сцени ігноруються.

### Безпека

**Шифрування захищає від звичайних користувачів, не від реверс-інженерів.** Ключі в PlayerPrefs (реєстр Windows). Технічно підкований гравець дістане ключі. Для боротьби з читингом потрібна server-side валідація.

---

## 31. Типові помилки та як їх уникнути

**Нестабільний SaveId.** Не використовуйте `GetInstanceID()` чи індекс — вони міняються. Використовуйте константи або `[Saveable("explicit_id")]`.

**Два об'єкти з однаковим SaveId.** Console покаже warning. Виправлення: суфікси (`"door_left"`, `"door_right"`) або динамічний ID (`override SaveId => $"npc_{_uniqueId}"`).

**Зміна `[Key(N)]` у вже виданому коді.** Якщо у `SaveFile` або `SaveTable` поміняти порядок `[Key]` — старі файли стануть нечитабельними. Підіймайте версію.

**Підняти CurrentVersion без мігратора.** Стара версія не зміниться, але дані залишаться як у попередній. Завжди пишіть мігратор разом з підняттям версії.

**Куб після Mode 2 Load без меша.** Об'єкт створено через `GameObject.CreatePrimitive` без `PrimitiveHelper` і не є префабом. Console покаже warning `'has a Renderer but no prefab source'`. Виправлення: `PrimitiveHelper.CreatePrimitive` або Convert to Saveable Prefab.

**Mode 2: префаб не знайдено в реєстрі.** PrefabRegistry не створено або не прив'язано до SaveManager. У білді — переконайтесь, що на префабі є `PrefabSourceMarker`.

**Source Generator не запускається.** Перевірте: Unity 2021.2+, DLL з міткою `RoslynAnalyzer`, зняті всі Platform-галочки. Помилка `Microsoft.CodeAnalysis cannot be resolved` означає, що мітка не застосована.

**Load не відновлює стан.** Об'єкт не зареєстрований у SaveRegistry. Перевірте: успадковує `SaveableBehaviour`, або має `[Saveable]`, або викликає `Register/Unregister` вручну в `OnEnable/OnDisable`.

**`.meta` конфлікт з Unity.** Стара версія системи зберігала метадані з розширенням `.meta`. Виправлено: тепер `.savmeta`. Якщо в проекті виникли помилки `The .meta file ... does not have a valid GUID` — закрийте Unity, видаліть стару папку `Assets/SaveData/` цілком, оновіть SaveManager до версії з `.savmeta` і папкою `SaveData~`.

**Rollback повертає не те, що очікувано.** Перевірте `{slot}_snap_*.sav` у папці. Якщо тогл Rollback щойно включили — старих знімків ще немає, перший Save стане snap_0. Також пам'ятайте: кожен slotId має власну чергу.

**Забути `[MessagePackObject]` на кастомному класі.** Якщо зберігаєте власну структуру через `Set("data", myObject)` — додайте `[MessagePackObject(true)]` або переконайтесь, що активний `ContractlessStandardResolver` (це за замовчуванням).
