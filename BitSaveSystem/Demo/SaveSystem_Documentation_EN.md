# Save System Documentation

> Complete usage guide with an explanation of each script's logic.

---

## Contents

1. [General Architecture](#1-general-architecture)
2. [How the System Works — Step by Step](#2-how-the-system-works--step-by-step)
3. [Two Save Modes: State and FullScene](#3-two-save-modes-state-and-fullscene)
4. [Attributes: Saveable, Track, SaveIgnore](#4-attributes-saveable-track-saveignore)
5. [ISaveable — The Contract for All Objects](#5-isaveable--the-contract-for-all-objects)
6. [SaveTable — Data Table](#6-savetable--data-table)
7. [Source Generator — Auto-generating OnSave / OnLoad](#7-source-generator--auto-generating-onsave--onload)
8. [SaveableBehaviour — Auto-registration Without the Generator](#8-saveablebehaviour--auto-registration-without-the-generator)
9. [SaveRegistry — Static Object Registry](#9-saveregistry--static-object-registry)
10. [HashService — Dirty Checking via Hashing](#10-hashservice--dirty-checking-via-hashing)
11. [SaveFile and SceneSnapshot — File Models](#11-savefile-and-scenesnapshot--file-models)
12. [SaveSerializer — MessagePack + LZ4](#12-saveserializer--messagepack--lz4)
13. [EncryptionService — AES-256-CBC + HMAC-SHA256](#13-encryptionservice--aes-256-cbc--hmac-sha256)
14. [SceneCapture and SceneRestore — Mode 2](#14-scenecapture-and-scenerestore--mode-2)
15. [PrefabRegistry and PrefabSourceMarker — Scenario A](#15-prefabregistry-and-prefabsourcemarker--scenario-a)
16. [PrimitiveHelper and PrimitiveTypeMarker — Scenario B](#16-primitivehelper-and-primitivetypemarker--scenario-b)
17. [SaveablePrefabConverter — Editor Tool](#17-saveableprefabconverter--editor-tool)
18. [SceneObjectId — Stable GUID for GameObjects](#18-sceneobjectid--stable-guid-for-gameobjects)
19. [MigrationManager — Versioning and Migration](#19-migrationmanager--versioning-and-migration)
20. [AutoSaveService — Auto-saving](#20-autosaveservice--auto-saving)
21. [RollbackService — Rollback System](#21-rollbackservice--rollback-system)
22. [ScreenshotService — Screenshots](#22-screenshotservice--screenshots)
23. [SaveLog — Diagnostics](#23-savelog--diagnostics)
24. [SaveManager — Main Orchestrator](#24-savemanager--main-orchestrator)
25. [Save Inspector Window — Editor UI](#25-save-inspector-window--editor-ui)
26. [Step-by-Step: Add a New Object](#26-step-by-step-add-a-new-object)
27. [Step-by-Step: Add a New Format Version](#27-step-by-step-add-a-new-format-version)
28. [Step-by-Step: Set Up Mode 2 in Your Project](#28-step-by-step-set-up-mode-2-in-your-project)
29. [Complete Data Flow: Save and Load](#29-complete-data-flow-save-and-load)
30. [System Limitations](#30-system-limitations)
31. [Common Mistakes and How to Avoid Them](#31-common-mistakes-and-how-to-avoid-them)

---

## 1. General Architecture

The system is built on the principle of **"orchestrator + independent services."** Each script has one clearly defined responsibility, and none of them know about the implementation details of the others.

```
┌────────────────────────────────────────────────────────────────┐
│                        SaveManager                             │  ← single entry point
│  (Singleton, MonoBehaviour, orchestrator)                      │
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
│                       SaveRegistry                             │  ← static registry
│  HashSet<ISaveable> + Dictionary<SaveId, ISaveable>            │
└────────────────────────────────────────────────────────────────┘

   Game objects (self-register via OnEnable):
   ┌────────────────┐  ┌──────────────┐  ┌────────────────┐
   │  PlayerHealth  │  │  Inventory   │  │   QuestLog     │
   │  [Saveable]    │  │ SaveableBehv │  │  manual        │
   │  [Track] fields│  │  (no gener.) │  │  ISaveable     │
   └────────────────┘  └──────────────┘  └────────────────┘
```

**Key idea:** `SaveManager` doesn't know about any specific game class and doesn't search for objects via `FindObjectsOfType`. Each object registers itself in `SaveRegistry` when enabled and removes itself when disabled. `SaveManager` simply reads the registry.

**Two layers of data:**
- **Logical state** (HP, gold, opened doors) — through `ISaveable` and `SaveTable`. Works in both modes.
- **Scene structure** (which objects exist, where they are, which components they have) — through `SceneCapture`/`SceneRestore`. Only in Mode 2.

---

## 2. How the System Works — Step by Step

### When "Save" is pressed (Mode 1)

1. The coder calls `SaveManager.Instance.Save("slot_1")`.
2. SaveManager starts the `SaveCoroutine`. A coroutine is needed because the screenshot requires `WaitForEndOfFrame`.
3. Instead of `FindObjectsOfType`, SaveManager reads `SaveRegistry.All` — an instant O(1) operation.
4. For each `ISaveable`, `ComputeStateHash()` is computed. If the hash matches the previous one, the table is taken from the previous file (lazy loading via `prevFile ??= ...`).
5. For changed objects, `OnSave()` is called, returning a `SaveTable`.
6. `SaveFile` is serialized via MessagePack + LZ4.
7. The bytes are encrypted via AES-256-CBC + HMAC-SHA256.
8. Atomic write to disk: `.tmp` → `File.Replace` → `.sav` (the old one becomes `.bak`).
9. A `.savmeta` file is written separately — without encryption, for the UI.
10. A snapshot is added to the Rollback queue.

### When "Save Full Scene" is pressed (Mode 2)

Everything above, plus before serialization:

3a. `SceneCapture.Capture()` recursively traverses the entire scene and for each `GameObject` decides:
- **If it's a prefab instance** → saves the `prefabGuid` + a delta of changed fields.
- **If it has a PrimitiveTypeMarker** → saves all components via reflection.
- **Otherwise** → also reflection, but with a warning in the Console (because the mesh won't be restored on Load).

The result is placed into `SaveFile.Scene`, then it follows the same serialization path.

### When loading

The same path in reverse:
1. `File.ReadAllBytes` → bytes.
2. `EncryptionService.Decrypt` → plain bytes. If the HMAC doesn't match — fallback to `.bak`.
3. `SaveSerializer.Deserialize` → `SaveFile`.
4. `MigrationManager.Migrate` — if the version is older.
5. `ApplyFile`:
   - If it's Mode 2 → `SceneRestore.Apply` reconstructs the scene.
   - For each table, look up the `ISaveable` in the registry by `SaveId` and call `OnLoad`.

---

## 3. Two Save Modes: State and FullScene

| Mode | API | What it saves | When to use |
|------|-----|---------------|-------------|
| **Mode 1: State** | `Save(slot)` | Only the state of marked `ISaveable` objects | The scene structure is fixed; only object attributes change (HP, inventory, quest progress). |
| **Mode 2: FullScene** | `SaveFullScene(slot)` | All scene GameObjects + their components + state | A dynamic scene (enemies, corpses, picked-up items, opened chests). |

**Mode 1 — fast and compact.** File is ~1–10 KB. It doesn't remember how many enemies are in the scene — it only remembers the player's HP, gold, flags.

**Mode 2 — detailed but more expensive.** File can be 50–500 KB for a typical scene. It remembers every GameObject that existed at the time of Save.

Both modes can be used in one project with different slotIds:
- `Save("autosave")` — fast Mode 1 every 30 seconds.
- `SaveFullScene("checkpoint")` — Mode 2 at checkpoints.

---

## 4. Attributes: Saveable, Track, SaveIgnore

Three attributes for marking classes and fields.

**`[Saveable]`** — marks a class as one whose state is saved. The Source Generator will generate `OnSave`, `OnLoad`, `SaveId`, `ComputeStateHash`, and registration.

```csharp
[Saveable("player_health")]           // ID = "player_health"
public partial class PlayerHealth : MonoBehaviour { ... }

[Saveable]                             // ID = "PlayerHealth" (class name)
public partial class PlayerHealth : MonoBehaviour { ... }

[Saveable(IncludeAllPublicFields = true)]
public partial class Inventory : MonoBehaviour
{
    public int Gold;                    // included
    public List<string> Items;          // included
    [SaveIgnore] public float TempBuff; // excluded
}
```

The class must be `partial`, because the Source Generator adds the interface implementation via `partial class`.

**`[Track]`** — marks a field or property as one whose state is saved. An alternative to `IncludeAllPublicFields`.

```csharp
[Saveable]
public partial class PlayerHealth : MonoBehaviour
{
    [Track("hp")]    public float CurrentHp;  // table key = "hp"
    [Track]          public bool  IsAlive;    // key = "IsAlive"
    public string DebugName;                  // NOT saved (no [Track])
}
```

**`[SaveIgnore]`** — excludes a field, even if the class has `IncludeAllPublicFields = true`.

---

## 5. ISaveable — The Contract for All Objects

`ISaveable` is the only interface that unifies the system. Any class that saves state implements it.

```csharp
public interface ISaveable
{
    string SaveId { get; }              // stable ID between sessions
    ulong  ComputeStateHash();           // FNV-1a hash of current state
    SaveTable OnSave();                  // builds a table with data
    void OnLoad(SaveTable table);        // restores state from a table
}
```

**`SaveId`** — a string, stable between sessions. Not `GetInstanceID()`. Use a constant (`"player_health"`) or `[Saveable("explicit_id")]`.

**`ComputeStateHash()`** — returns an FNV-1a 64-bit hash of the serialized bytes of all Track fields. If the hash hasn't changed between Saves, the object is skipped (Dirty-skip). The Source Generator generates this method automatically.

**`OnSave()`** — builds a `SaveTable`. The Source Generator generates it automatically, or you write it manually.

**`OnLoad(SaveTable table)`** — restores state. If a field is missing — `table.Get<T>(key, default)` returns the default value. This ensures backward compatibility with old saves without migration.

---

## 6. SaveTable — Data Table

`SaveTable` is a "key → serialized bytes" structure. Each `ISaveable` has its own table. Inside is a `Dictionary<string, byte[]>`.

### Creating a table

```csharp
SaveTable.From(this);                    // key = class name
SaveTable.From(this, "left_door");       // key = "ClassName.left_door"
SaveTable.From("global_settings");       // an arbitrary string
```

### Writing via the fluent API

```csharp
return SaveTable.From(this)
    .Set("hp", currentHp)            // float
    .Set("items", items)             // List<string>
    .SetVector3("pos", transform.position)
    .SetQuaternion("rot", transform.rotation)
    .SetColor("color", renderer.material.color);
```

`Set<T>` supports any MessagePack-compatible type: primitives, strings, arrays, `List<T>`, `Dictionary<K,V>`, custom structs.

For Unity types (`Vector3`, `Quaternion`, `Color`) there are dedicated methods — they serialize as `float[]`, which is more compact than tagging Unity structs with `[MessagePackObject]`.

### Reading with a default

```csharp
hp = table.Get<float>("hp", 100f);
items = table.Get<List<string>>("items", new List<string>());
position = table.GetVector3("pos", Vector3.zero);
```

If the key is missing or the type doesn't match — the default is returned. No exceptions.

---

## 7. Source Generator — Auto-generating OnSave / OnLoad

The Source Generator is a Roslyn compilation component that automatically creates the `ISaveable` implementation for classes with `[Saveable]`.

### Two forms of usage

**Form 1: full package**

```csharp
[Saveable("player_health")]
public partial class PlayerHealth : MonoBehaviour
{
    [Track("hp")]    public float CurrentHp = 100f;
    [Track]          public bool  IsAlive   = true;
    [SaveIgnore]     public float TempBuff;
}
```

Will generate:
- `string SaveId => "player_health";`
- `ulong ComputeStateHash()` — FNV-1a over all Track fields
- `SaveTable OnSave()` — packs all fields
- `void OnLoad(SaveTable t)` — unpacks them
- `private void OnEnable() => SaveRegistry.Register(this);`
- `private void OnDisable() => SaveRegistry.Unregister(this);`

The coder doesn't have to write anything — not even the registration.

**Form 2: helper methods only**

```csharp
public partial class CustomLogic : MonoBehaviour, ISaveable
{
    [Track("score")] private int _score;

    public string SaveId => "custom_logic";
    public ulong ComputeStateHash() => ComputeFieldsHash();   // generated
    public SaveTable OnSave()
    {
        var t = SaveTable.From(this);
        WriteFieldsTo(t);                                       // generated
        return t;
    }
    public void OnLoad(SaveTable t) => ReadFieldsFrom(t);       // generated

    private void OnEnable()  => SaveRegistry.Register(this);
    private void OnDisable() => SaveRegistry.Unregister(this);
}
```

Here the generator adds only three helper methods (`ComputeFieldsHash`, `WriteFieldsTo`, `ReadFieldsFrom`), and the coder writes the main logic.

### How to install the generator

The Source Generator is a separate DLL compiled with .NET Standard 2.0. Location: `Assets/BitSaveSystem/SourceGenerator/SaveSystem.SourceGenerator.dll`. The DLL must have the `RoslynAnalyzer` label and all Platform checkboxes unchecked.

If the generator is not installed — a class with `[Saveable]` won't compile. The alternative is `SaveableBehaviour` (see the next section).

### Requirements

- Unity 2021.2+
- DLL with the `RoslynAnalyzer` label
- The class must be `partial`

---

## 8. SaveableBehaviour — Auto-registration Without the Generator

If the Source Generator is not installed, or you need full control over OnSave/OnLoad — inherit from `SaveableBehaviour`.

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

    // ComputeStateHash() — fallback via OnSave + a hash of the serialized table.
    // Slower than the generator, but works without attributes.
}
```

`SaveableBehaviour.OnEnable/OnDisable` automatically register/unregister the object in `SaveRegistry`. If you need to override them — call `base.OnEnable()`.

---

## 9. SaveRegistry — Static Object Registry

`SaveRegistry` replaced `FindObjectsOfType`. A static class with a `HashSet<ISaveable>` + a `Dictionary<string, ISaveable>` for lookup by SaveId.

**Why a HashSet:** Register/Unregister is O(1). Duplicates are ignored automatically.

**Why static:** the registry must exist independently of any GameObject — it's the global state of the system.

### Public API

```csharp
SaveRegistry.All                            // IReadOnlyCollection<ISaveable>
SaveRegistry.Count                          // number of registered objects
SaveRegistry.Find("player_health")          // ISaveable or null
SaveRegistry.AnyDirty(prevHashes)           // whether anything changed since prevHashes
SaveRegistry.Clear()                        // clear on scene change
SaveRegistry.Register(saveable)
SaveRegistry.Unregister(saveable)
```

### Clearing on scene change

`SaveManager` subscribes to `SceneManager.sceneLoaded`. On a load in `Single` mode, the registry is cleared, and objects in the new scene register themselves. On `Additive` — the registry is left untouched (new objects are simply added).

### Duplicate detection

When registering a second object with the same `SaveId`, a warning appears in the Console:

```
[SaveSystem] Duplicate SaveId 'player_health' (PlayerHealth vs PlayerHealth).
Second overrides first.
```

The second object overwrites the first one's record in the Dictionary, but both remain in the HashSet.

---

## 10. HashService — Dirty Checking via Hashing

Instead of an `IsDirty` flag and `Tracked<T>` wrappers from the old system, an FNV-1a 64-bit hash of the serialized bytes is now used.

### How it works

On every Save, `ComputeStateHash()` is computed for each `ISaveable`:

```csharp
ulong hash = HashService.Fnv1a(MessagePackSerializer.Serialize(allTrackFields));
```

The hash is recorded in `SaveFile.Hashes` (Dictionary<saveId, hash>). On the next Save:

```csharp
foreach (var sv in SaveRegistry.All)
{
    ulong currentHash = sv.ComputeStateHash();
    if (previousHashes.TryGetValue(sv.SaveId, out var prevHash) && prevHash == currentHash)
    {
        // Object hasn't changed — take the table from the previous file
        file.Tables[sv.SaveId] = prevFile.Tables[sv.SaveId];
        continue;
    }
    // Changed — call OnSave
    file.Tables[sv.SaveId] = sv.OnSave();
}
```

### Advantages over Tracked<T>

- **Works with collections automatically.** `list.Add()` changes the list's hash — the system sees the change.
- **No need to wrap fields.** Just `[Track] public int Hp;` instead of `Tracked<int> Hp`.
- **You can't forget to set a flag.** State and hash are always in sync.
- **More expensive** — computing a hash is slower than reading a flag. But the difference is in microseconds.

---

## 11. SaveFile and SceneSnapshot — File Models

`SaveFile` is the root object that is serialized in full and written to disk.

```csharp
[MessagePackObject]
public sealed class SaveFile
{
    [Key(0)] public int    Version;
    [Key(1)] public long   TimestampUtc;
    [Key(2)] public string SceneName;
    [Key(3)] public string SlotId;
    [Key(4)] public SaveMode Mode;        // State or FullScene
    [Key(5)] public Dictionary<string, SaveTable> Tables;   // Mode 1
    [Key(6)] public SceneSnapshot Scene;  // Mode 2
    [Key(7)] public Dictionary<string, ulong> Hashes;       // for Dirty-skip
}
```

**Tables** is populated in both modes — for all ISaveable objects in the scene.

**Scene** is populated only in Mode 2 — it contains the full structure of all GameObjects.

**SceneSnapshot** contains a list of `SceneObject`:

```csharp
public sealed class SceneObject
{
    public string ObjectGuid;            // from SceneObjectId
    public string Name;
    public bool ActiveSelf;
    public string ParentGuid;
    public int Layer;
    public string Tag;
    public float[] LocalPosition;        // [x, y, z]
    public float[] LocalRotation;        // [x, y, z, w]
    public float[] LocalScale;

    public string PrefabGuid;            // if a prefab instance
    public List<ComponentDelta> ComponentDeltas;  // delta from the prefab

    public List<ComponentSnapshot> Components;    // fallback without a prefab
    public List<SaveTable> SaveableTables;        // from ISaveable on this GO
}
```

**SlotMeta** — a separate `.savmeta` file without encryption, for quick reading in the UI slot-selection menu (save time, scene name, presence of a screenshot).

---

## 12. SaveSerializer — MessagePack + LZ4

Responsible for converting `SaveFile` into bytes and back. A single MessagePack call with the `Lz4BlockArray` option does both steps.

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithCompression(MessagePackCompression.Lz4BlockArray)
    .WithResolver(ContractlessStandardResolver.Instance);

byte[] bytes = MessagePackSerializer.Serialize(file, options);
SaveFile file = MessagePackSerializer.Deserialize<SaveFile>(bytes, options);
```

**ContractlessStandardResolver** — allows serializing custom classes without `[MessagePackObject]`. Convenient for a prototype, slower than classes with attributes. For production code, add `[MessagePackObject(true)]` to your structs.

**Lz4BlockArray** — LZ4 compression with a block structure. 5–10 times faster than GZip at decompression. For a typical save, ~3–5x more compact than uncompressed MessagePack.

Approximate sizes for a save with 10 ISaveable objects:
- JSON without compression: ~4000 bytes
- MessagePack without LZ4: ~1200 bytes
- MessagePack + LZ4: ~600 bytes

### ToDebugJson

```csharp
string json = serializer.ToDebugJson(bytes);
```

Converts MessagePack bytes into readable JSON. Used only in the Save Inspector Window for the data preview.

---

## 13. EncryptionService — AES-256-CBC + HMAC-SHA256

### Why not AES-GCM

`AesGcm` throws `PlatformNotSupportedException` on Unity prior to 2021.2 and on some mobile targets. `Aes.Create()` (CBC mode) works **everywhere** without exceptions.

### Why CBC + HMAC

AES-CBC doesn't verify integrity — if someone changes the bytes, CBC will "decrypt" and return garbage without an error. So an **Encrypt-then-MAC** scheme is used:

1. Encrypt the data (CBC).
2. Sign the encrypted result with HMAC-SHA256 using a separate key.
3. On decryption, the HMAC is verified first. If it doesn't match — `CryptographicException`, and SaveManager switches to `.bak`.

### Encrypted file structure

```
[hmac: 32 bytes][iv: 16 bytes][ciphertext: N bytes]
```

The IV is generated by a cryptographically secure generator on each encryption — identical data is encrypted differently every time.

### ConstantTimeEquals

The HMAC comparison is performed in **fixed time** (XOR of all bytes) to protect against timing attacks. Don't use `==` to compare cryptographic values.

### Two keys in PlayerPrefs

Stored under `SS_EncKey_v3` (AES) and `SS_MacKey_v3` (HMAC). The `_v3` suffix distinguishes them from previous versions.

### Custom keys

```csharp
byte[] encKey = ...; // 32 bytes
byte[] macKey = ...; // 32 bytes
var service = new EncryptionService(encKey, macKey);
```

Useful for cloud saves: identical keys across all of the user's devices (e.g., derived from the account).

---

## 14. SceneCapture and SceneRestore — Mode 2

The largest and most complex component of the system. It captures and restores the full scene structure.

### SceneCapture.Capture()

Traverses the scene recursively via `Scene.GetRootGameObjects()`. For each GameObject:

1. Ensures a `SceneObjectId` is present (adds one if missing).
2. Fills in the base data: name, active, parent, layer, tag, transform.
3. Collects a `SaveTable` from all `ISaveable` components.
4. **Component-saving strategy:**
   - **If there's a prefab (via `PrefabUtility.GetCorrespondingObjectFromOriginalSource` or `PrefabSourceMarker`)** → save the `PrefabGuid` + a field delta via `CaptureDeltas()`.
   - **Otherwise** → serialize all components via reflection (`CaptureAllComponents`).
   - If the object has a `Renderer` but no prefab and no `PrimitiveTypeMarker` — `WarnIfNotRestorable()` issues a warning.

### SceneRestore.Apply()

1. Collects a map of existing GameObjects in the scene by `SceneObjectId.Guid`.
2. For each `SceneObject` from the snapshot:
   - **If it exists in the scene** → update the state.
   - **If there's a PrefabGuid** → `Instantiate(prefab)` + apply the delta.
   - **If it's missing, but there's a PrimitiveTypeMarker among the saved components** → `PrimitiveHelper.CreatePrimitive(SavedType, SavedColor)`. Unique logic — it calls `GameObject.CreatePrimitive`, which itself adds `MeshFilter`+`MeshRenderer`+`Collider` with references to Unity's built-in assets.
   - **Otherwise** → `new GameObject(name)` + `AddComponent` for each saved component (without mesh/material).
3. Sets up parent relationships in a second pass (once all objects are created).
4. Calls `OnLoad` for all `ISaveable` objects on the GO.
5. If `destroyMissing=true` — removes GameObjects from the scene that aren't in the snapshot.

### ComponentReflector

A helper class that reads and writes Unity component fields via reflection. Rules:

- Only public fields and fields with `[SerializeField]`.
- Without `[SaveIgnore]` and `[NonSerialized]`.
- Supported types: primitives, strings, enum, `Vector2/3/4`, `Quaternion`, `Color`, `Rect`, `Bounds`, arrays, `List<T>`.
- **`UnityEngine.Object` references are not serialized** (Material, Mesh, Texture, AudioClip) — this is a limitation by design.

A `Dictionary<Type, FieldInfo[]>` cache ensures reflection runs once per type.

---

## 15. PrefabRegistry and PrefabSourceMarker — Scenario A

**PrefabRegistry** — a ScriptableObject with a `prefabGuid → GameObject` map. Created:

- Automatically on the first call to `SaveablePrefabConverter` (the Editor tool).
- Manually via `Assets → Create → SaveSystem → Prefab Registry`.

`PrefabRegistryPostprocessor` (Editor-only) automatically adds every imported prefab to the registry, if the registry exists.

**PrefabSourceMarker** — a component placed on a prefab that stores its `PrefabGuid`. Needed for builds: in a build, `PrefabUtility.GetCorrespondingObjectFromOriginalSource` is unavailable, so SceneCapture reads the `PrefabGuid` from this marker.

The `Convert to Saveable Prefab` tool adds `PrefabSourceMarker` automatically.

### How Save works with a prefab

```
SceneCapture sees the GO
    → finds the prefab via PrefabUtility (Editor) or PrefabSourceMarker (Build)
    → finds the PrefabGuid in the PrefabRegistry
    → ComponentReflector.CaptureDelta(instanceComp, prefabComp) for each component
    → saves only the fields that differ
```

Size per object: 50–200 bytes (delta only). Without a prefab — 500–2000 bytes.

---

## 16. PrimitiveHelper and PrimitiveTypeMarker — Scenario B

For runtime primitives (Cube, Sphere, Capsule, etc.) that you create from code without a prefab.

```csharp
// Instead of this:
var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
go.GetComponent<Renderer>().material.color = Color.red;
// (after Save Full Scene + Load — it'll be a "bare" GameObject without a mesh)

// Do this:
var go = PrimitiveHelper.CreatePrimitive(PrimitiveType.Cube, Color.red);
// (PrimitiveTypeMarker added automatically — Mode 2 restores it correctly)
```

**PrimitiveTypeMarker** stores two fields:
- `PrimitiveType SavedType` (enum)
- `Color SavedColor` (via `SetColor` → float[4])

Both are primitive values that are serialized by reflection without problems.

On Load, `SceneRestore` sees `PrimitiveTypeMarker` among the saved components, reads `SavedType`, and calls `GameObject.CreatePrimitive(SavedType)`. Unity itself adds `MeshFilter`+`MeshRenderer`+`Collider` from built-in assets (`Cube.fbx`, `Default-Material`). Then `PrimitiveHelper.CreatePrimitive` adds the marker itself. The color is applied from `SavedColor`.

### Limitations

Only standard Unity primitives are supported. More complex materials (texture, shader, metallic, normal map) are not saved. For those, use Scenario A.

---

## 17. SaveablePrefabConverter — Editor Tool

An Editor-only utility for one-click conversion of a scene object into a saveable prefab.

**Access:** right-click on a GameObject in the Hierarchy → **SaveSystem → Convert to Saveable Prefab**.

**What it does:**

1. Ensures a `PrefabRegistry` exists in the project (creates `Assets/SaveablePrefabs/PrefabRegistry.asset` if missing).
2. Ensures the `Assets/SaveablePrefabs/` folder exists.
3. Calls `PrefabUtility.SaveAsPrefabAssetAndConnect()` — creates the prefab and turns the scene instance into a prefab instance.
4. Adds `PrefabSourceMarker` to the prefab itself with its GUID.
5. Registers the prefab in `PrefabRegistry`.
6. If the scene has a `SaveManager` with an empty `Prefab Registry` field — binds it automatically.

After this, the object is ready for Mode 2 saving under Scenario A.

---

## 18. SceneObjectId — Stable GUID for GameObjects

A component that stores a GUID for each GameObject. Needed for Mode 2, so that after Load it can find the "same" object in the scene that existed at the time of Save.

```csharp
[DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
public sealed class SceneObjectId : MonoBehaviour
{
    [SerializeField] private string _guid;
    public string Guid => _guid;
}
```

- In the Editor, `OnValidate()` automatically assigns a GUID for scene objects (not for prefab assets).
- On `Instantiate(prefab)` at runtime — the `Guid` getter creates a new GUID lazily.
- During SceneCapture/Restore — it's used as the key for matching snapshot ↔ scene.

### Why DefaultExecutionOrder = -10000

So the GUID is initialized before any other Awake accesses it.

---

## 19. MigrationManager — Versioning and Migration

On load, `SaveManager` calls `MigrationManager.Migrate(file, currentVersion)`. If `file.Version < currentVersion` — a chain of migrators runs.

```csharp
public interface IMigrator
{
    int FromVersion { get; }
    int ToVersion   { get; }
    SaveFile Migrate(SaveFile file);
}
```

Registration in Bootstrap:

```csharp
MigrationManager.Register(new MigratorV1toV2());
MigrationManager.Register(new MigratorV2toV3());
```

When loading v1 with currentVersion=3 — it runs v1→v2→v3 automatically.

If there's no migrator for some step — an error appears in the Console, but loading doesn't crash (data from missing fields is simply taken from the default).

---

## 20. AutoSaveService — Auto-saving

A timer that periodically calls Save through a delegate passed in from `SaveManager`.

### Modes

- **Interval** — every N seconds (5–3600, in `AutoSaveConfig.IntervalSeconds`).
- **OnTrigger** — only on `TriggerAutoSave()`. For a checkpoint system.
- **Hybrid** — both.
- **Disabled** — manual saving only.

### Dirty checking

If `SkipIfNothingChanged = true` — before saving, `SaveRegistry.AnyDirty(previousHashes)` is called. If no ISaveable has changed — Save is not performed.

### AutoSaveConfig

A ScriptableObject. Create it via `Assets → Create → SaveSystem → AutoSave Config`. Bind it to the `Auto Save Config` field on SaveManager in the Inspector.

Or programmatically:

```csharp
var cfg = ScriptableObject.CreateInstance<AutoSaveConfig>();
cfg.Mode = AutoSaveMode.Interval;
cfg.IntervalSeconds = 60f;
cfg.SaveMode = SaveMode.State;       // or FullScene
cfg.SlotId = "autosave";
cfg.SkipIfNothingChanged = true;
SaveManager.Instance.StartAutoSave(cfg);
```

---

## 21. RollbackService — Rollback System

Keeps a queue of the last N snapshots for each slot. After overflow, the oldest is removed from both memory and disk.

Files: `{slotId}_snap_0.sav`, `{slotId}_snap_1.sav`, ..., `{slotId}_snap_{N-1}.sav`. Stored already encrypted.

```csharp
SaveManager.Instance.Rollback("slot_1", stepsBack: 1);  // one save back
SaveManager.Instance.Rollback("slot_1", stepsBack: 3);  // three back
```

If the request is for more steps than there are snapshots — the oldest available one is returned.

Works with both modes (Mode 1 and Mode 2), but **each slotId has its own queue**. `Rollback("game")` won't affect `Rollback("game_scene")`.

---

## 22. ScreenshotService — Screenshots

A coroutine that:

1. `yield return new WaitForEndOfFrame()` — mandatory; wait for the end of the frame.
2. `Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture()`.
3. (Optionally) Resizes to `maxWidth` via `Graphics.Blit` + `RenderTexture`.
4. `tex.EncodeToJPG(quality)` — quality=60 by default, ~30–80 KB.
5. `File.WriteAllBytes` next to the main save.
6. `Object.Destroy(tex)` — frees the texture.

The call goes through the `Save` parameters:

```csharp
SaveManager.Instance.Save("slot_1", captureScreenshot: true);
// → slot_1.jpg

SaveManager.Instance.Save("slot_1", captureScreenshot: true, screenshotName: "boss_fight");
// → boss_fight.jpg
```

The coder sets `captureScreenshot: true` — otherwise no screenshot is taken (the default is `false`).

---

## 23. SaveLog — Diagnostics

A ring buffer of the last N operations (500 by default). Each entry contains:

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
    public string Stack;                  // short stack trace
    public bool Encrypted, Compressed;
    public int Version;
    public string DataPreview;            // JSON for the Editor
    public string Note;
    public bool Success;
    public string Error;
    public double DurationMs;
}
```

CallSite is obtained via `StackTrace` while skipping SaveManager's internal frames. This lets the UI show which script and line the Save was called from.

`SaveLog.OnEntryAdded` — an event that the Save Inspector Window subscribes to for real-time UI updates.

---

## 24. SaveManager — Main Orchestrator

A Singleton MonoBehaviour that holds all services and provides the public API.

### Inspector

| Field | Description |
|-------|-------------|
| Custom Save Path | Where to write. If empty — Application.persistentDataPath. |
| Use Assets Folder In Editor | In the Editor, writes to `Assets/SaveData~/`. The tilde means Unity ignores the folder. |
| Encryption Enabled | AES-256-CBC + HMAC. |
| Compression Enabled | LZ4. |
| Rollback Enabled | Create snap files. |
| Max Snapshots | Rollback queue size. |
| Current Version | Format version for migration. |
| Prefab Registry | For Mode 2. |
| Auto Save Config | For auto-saving. |

Everything is available programmatically via the `Instance.EncryptionEnabled` properties, etc.

### Public API

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

### Atomic write

```csharp
private static void WriteAtomic(string path, byte[] data)
{
    string tmp = path + ".tmp";
    string bak = path + ".bak";
    File.WriteAllBytes(tmp, data);
    if (File.Exists(path))
        File.Replace(tmp, path, bak);  // atomic: tmp→main, main→bak
    else
        File.Move(tmp, path);          // first Save — File.Replace requires a destination
}
```

### Lazy loading of the previous file

During Save with a hash cache, objects that haven't changed take their table from the previous file. The file is read at most once:

```csharp
prevFile = TryReadExisting(slotId);  // null if missing or corrupted
```

### File structure on disk

```
{SaveDir}/
├── slot_1.sav           ← main (encrypted)
├── slot_1.sav.bak       ← backup
├── slot_1.savmeta       ← metadata (not encrypted)
├── slot_1.jpg           ← screenshot
├── slot_1_snap_0.sav    ← rollback (newest)
├── slot_1_snap_1.sav
├── slot_1_snap_2.sav
├── autosave.sav
└── autosave.savmeta
```

`.savmeta` (not `.meta`) — to avoid conflicting with Unity's asset-meta file format.

`Assets/SaveData~/` (with a tilde) — so Unity ignores the folder (doesn't import the files as assets).

---

## 25. Save Inspector Window — Editor UI

An Editor-only window: **Window → Save System → Inspector**.

It shows:

- **A list of files** in the save folder: name, size, last modification time. The Reveal button opens the folder in the file explorer.
- **An operation log**: all Save, Load, AutoSave, Rollback, ScreenshotCapture, Encryption, Decryption, Compression, Migration.
- **Details of the selected entry on the right**: time, operation, slot, mode, file, size, whether encrypted/compressed, version, **call-site** (where it was called from), stack, duration, **JSON preview** of the data.

**Filters:**
- Search — text search across slot, callsite, file, note.
- Op — filter by operation type.
- Slot — filter by a specific slot.
- Mode — filter by State / FullScene.

---

## 26. Step-by-Step: Add a New Object

### Variant A: via the Source Generator (recommended)

**Step 1.** Make the class `partial` and add `[Saveable]`:

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

**Step 2.** Done. The Source Generator will generate `OnSave`, `OnLoad`, `SaveId`, `ComputeStateHash`, and `OnEnable/OnDisable` with registration.

### Variant B: via SaveableBehaviour (without the generator)

**Step 1.** Inherit from `SaveableBehaviour`:

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

Registration is automatic via `SaveableBehaviour.OnEnable`.

### Variant C: manual ISaveable implementation

If you can't inherit from `SaveableBehaviour`:

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

## 27. Step-by-Step: Add a New Format Version

**Step 1.** Bump `SaveManager.CurrentVersion` (e.g., from 2 to 3).

**Step 2.** Write a migrator:

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

**Step 3.** Register the migrator somewhere in Bootstrap:

```csharp
public class Bootstrap : MonoBehaviour
{
    private void Awake()
    {
        MigrationManager.Register(new MigratorV1toV2());
        MigrationManager.Register(new MigratorV2toV3());  // ← added
    }
}
```

Done. Old saves will automatically go through v1→v2→v3 on the next load.

---

## 28. Step-by-Step: Set Up Mode 2 in Your Project

**Step 1.** Create a `PrefabRegistry` via `Assets → Create → SaveSystem → Prefab Registry`. Or just click `Convert to Saveable Prefab` on any object in the Hierarchy — the registry will be created automatically.

**Step 2.** Bind the `PrefabRegistry` to the `Prefab Registry` field on the `SaveManager` component. The Convert tool does this automatically if the field is empty.

**Step 3.** For all prefabs that may appear in the scene dynamically (enemies, loot, projectiles) — make sure that:
- The prefab is imported (PrefabRegistryPostprocessor will add it to the registry itself).
- The prefab has a `PrefabSourceMarker` component (Convert adds it; for existing ones — add it manually).

**Step 4.** For runtime primitives, use `PrimitiveHelper.CreatePrimitive` instead of `GameObject.CreatePrimitive`.

**Step 5.** Call `SaveManager.Instance.SaveFullScene("slot")` and `Load("slot")`.

---

## 29. Complete Data Flow: Save and Load

### Save (Mode 1)

```
Save("slot_1")
    └─► SaveCoroutine()
            ├─► (optionally) WaitForEndOfFrame → CaptureScreenshot()
            ├─► SaveRegistry.All
            ├─► for each ISaveable:
            │       ├─► hash = sv.ComputeStateHash()
            │       ├─► if hash == prevHashes[sv.SaveId] → table from prevFile (lazy)
            │       └─► else → sv.OnSave() → SaveTable
            ├─► new SaveFile { Tables, Hashes, Mode = State }
            ├─► SaveSerializer.Serialize(file)        → byte[]  (MessagePack + LZ4)
            ├─► EncryptionService.Encrypt(bytes)      → byte[]  (AES + HMAC)
            ├─► WriteAtomic(path, bytes)
            │       ├─► first time: .tmp → File.Move  → .sav
            │       └─► subsequent: .tmp → File.Replace → .sav  (+ .bak)
            ├─► File.WriteAllBytes(MetaPath, meta)    → .savmeta (not encrypted)
            └─► RollbackService.Push(slotId, file)    → _snap_N.sav
```

### Save (Mode 2)

The same plus, before `Serialize`:

```
            ├─► SceneCapture.Capture(prefabRegistry):
            │       └─► for each GameObject in the scene:
            │               ├─► SceneObjectId — ensure it's present
            │               ├─► transform, name, active, parent, layer, tag
            │               ├─► from ISaveable components → SaveTable
            │               └─► component strategy:
            │                       ├─► prefab → PrefabGuid + ComponentDeltas
            │                       └─► no prefab → ComponentSnapshot for each
            │                                        + warn if there's a Renderer without a marker
            └─► file.Scene = snapshot
```

### Load

```
Load("slot_1")
    └─► LoadInternal()
            ├─► File.ReadAllBytes(SlotPath)
            ├─► EncryptionService.Decrypt(bytes)       → byte[]
            │       └─► CryptographicException → read .bak → Decrypt again
            ├─► SaveSerializer.Deserialize(bytes)      → SaveFile
            ├─► MigrationManager.Migrate(file, current) → possibly updated
            └─► ApplyFile(file):
                    ├─► if Mode == FullScene → SceneRestore.Apply(file.Scene, registry)
                    │       ├─► create/update GO (prefab / marker / new GO)
                    │       ├─► transform, active, layer, tag
                    │       ├─► parent-child (in a second pass)
                    │       ├─► OnLoad for ISaveable on the GO
                    │       └─► destroyMissing — remove GOs not in the snapshot
                    └─► for each table in file.Tables:
                            └─► SaveRegistry.Find(saveId).OnLoad(table)
```

---

## 30. System Limitations

### Mode 2

**Serializing meshes and materials.** The system does NOT serialize `Mesh`, `Material`, `Texture`, `AudioClip`, or other `UnityEngine.Object` references. They are restored only through a prefab (Scenario A) or `PrimitiveTypeMarker` (Scenario B).

**Material.color at runtime.** `renderer.material.color = ...` creates an instance copy of the material that is lost on Stop Play. For runtime colors, use `PrimitiveHelper`.

**DontDestroyOnLoad objects** in Mode 2 are captured only if they are in the active scene.

### Mode 1

**Only marked ISaveable objects.** If you forgot to register a component — its state won't be saved.

**The Save/Load order between objects is not guaranteed.** If A depends on B during OnLoad — use `SaveRegistry.Find` inside OnLoad for lazy reading.

### Transferring saves between devices

**HMAC key is per-machine.** By default, EncryptionService generates keys in this computer's PlayerPrefs. A save from one PC won't decrypt on another.

For cloud saves: `new EncryptionService(encKey, macKey)` with identical keys on all devices.

### Size and performance

**Mode 2 with 1000+ objects:** Save is 100–500 ms, the file is hundreds of KB.

**Rollback with Mode 2:** each snap is a full scene snapshot. 5 snaps × 500 KB = 2.5 MB per slot.

**Dirty-skip doesn't optimize Mode 2.** The scene is always captured in full.

### Technical limitations

**The Source Generator requires Unity 2021.2+.** On older versions — only `SaveableBehaviour`.

**IL2CPP stripping.** Set `Managed Stripping Level = Minimal` or add a `link.xml`.

**WebGL.** `File.WriteAllBytes` works, but saves live only in the browser's IndexedDB.

**Multi-scene.** Only `GetActiveScene()` is captured. Other loaded scenes are ignored.

### Security

**Encryption protects against ordinary users, not reverse engineers.** The keys are in PlayerPrefs (the Windows registry). A technically skilled player can extract the keys. To fight cheating, server-side validation is needed.

---

## 31. Common Mistakes and How to Avoid Them

**Unstable SaveId.** Don't use `GetInstanceID()` or an index — they change. Use constants or `[Saveable("explicit_id")]`.

**Two objects with the same SaveId.** The Console will show a warning. Fix: suffixes (`"door_left"`, `"door_right"`) or a dynamic ID (`override SaveId => $"npc_{_uniqueId}"`).

**Changing `[Key(N)]` in already-shipped code.** If you change the order of `[Key]` attributes in `SaveFile` or `SaveTable` — old files become unreadable. Bump the version.

**Bumping CurrentVersion without a migrator.** The old version won't change, but the data will remain as in the previous one. Always write a migrator together with bumping the version.

**A cube without a mesh after a Mode 2 Load.** The object was created via `GameObject.CreatePrimitive` without `PrimitiveHelper` and is not a prefab. The Console will show the warning `'has a Renderer but no prefab source'`. Fix: `PrimitiveHelper.CreatePrimitive` or Convert to Saveable Prefab.

**Mode 2: prefab not found in the registry.** PrefabRegistry was not created or not bound to SaveManager. In a build — make sure the prefab has a `PrefabSourceMarker`.

**The Source Generator doesn't run.** Check: Unity 2021.2+, the DLL has the `RoslynAnalyzer` label, all Platform checkboxes are unchecked. The error `Microsoft.CodeAnalysis cannot be resolved` means the label is not applied.

**Load doesn't restore state.** The object is not registered in SaveRegistry. Check: it inherits `SaveableBehaviour`, or has `[Saveable]`, or calls `Register/Unregister` manually in `OnEnable/OnDisable`.

**`.meta` conflict with Unity.** The old version of the system saved metadata with the `.meta` extension. Fixed: now it's `.savmeta`. If errors like `The .meta file ... does not have a valid GUID` appeared in the project — close Unity, delete the old `Assets/SaveData/` folder entirely, update SaveManager to the version with `.savmeta` and the `SaveData~` folder.

**Rollback returns something other than expected.** Check the `{slot}_snap_*.sav` files in the folder. If you just enabled the Rollback toggle — there are no old snapshots yet, and the first Save becomes snap_0. Also remember: each slotId has its own queue.

**Forgetting `[MessagePackObject]` on a custom class.** If you save your own struct via `Set("data", myObject)` — add `[MessagePackObject(true)]` or make sure `ContractlessStandardResolver` is active (it is by default).
