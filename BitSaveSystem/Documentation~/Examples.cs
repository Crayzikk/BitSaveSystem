using SaveSystem;
using UnityEngine;

// ============================================================
// ВАРІАНТ A: повністю автоматично через [Saveable] + [Track]
// ============================================================
// Source Generator згенерує SaveId, ComputeStateHash,
// OnSave, OnLoad, OnEnable/OnDisable.
// Клас має бути PARTIAL.

[Saveable("player_health")]
public partial class PlayerHealth : MonoBehaviour
{
    [Track("hp")]    public float CurrentHp = 100f;
    [Track("max")]   public float MaxHp     = 100f;
    [Track("alive")] public bool  IsAlive   = true;
}

// ============================================================
// ВАРІАНТ Б: контроль вручну через SaveableBehaviour
// ============================================================
public class Inventory : SaveableBehaviour
{
    [SerializeField] private System.Collections.Generic.List<string> _items = new();
    [SerializeField] private int _gold;

    public override string SaveId => "inventory";

    public override SaveTable OnSave() => SaveTable.From(this)
        .Set("items", _items)
        .Set("gold",  _gold);

    public override void OnLoad(SaveTable t)
    {
        _items = t.Get<System.Collections.Generic.List<string>>("items", new());
        _gold  = t.Get<int>("gold", 0);
    }

    // ComputeStateHash наслідується (через серіалізацію OnSave).
    // Якщо хочеться швидше — додайте [Saveable] + [Track] на поля,
    // тоді генератор створить хеш по полях напряму.
}

// ============================================================
// ВАРІАНТ В: гібрид. Власний OnSave/OnLoad, але хеш-методи
// з генератора (приберігаємо швидкість).
// ============================================================
public partial class QuestTracker : MonoBehaviour, ISaveable
{
    [Track("chapter")] private int _chapter = 1;
    [Track("done")]    private System.Collections.Generic.List<string> _done = new();

    public string SaveId => "quest_tracker";

    // Генератор додасть ComputeFieldsHash, WriteFieldsTo, ReadFieldsFrom.
    public ulong ComputeStateHash() => ComputeFieldsHash();

    public SaveTable OnSave()
    {
        var t = SaveTable.From(this);
        WriteFieldsTo(t);
        // плюс щось своє кастомне:
        t.Set("computed_score", _done.Count * 100);
        return t;
    }

    public void OnLoad(SaveTable t)
    {
        ReadFieldsFrom(t);
        // нічого спеціального — computed_score не зчитуємо
    }

    private void OnEnable()  => SaveRegistry.Register(this);
    private void OnDisable() => SaveRegistry.Unregister(this);
}

// ============================================================
// Тригер збереження звідки завгодно
// ============================================================
public class GameMenu : MonoBehaviour
{
    public void OnSaveButtonClicked()
    {
        // Mode 1 — тільки стан, зі скріншотом, кастомне ім'я скріншота
        SaveManager.Instance.Save("slot_1", captureScreenshot: true, screenshotName: "checkpoint_1");
    }

    public void OnFullSnapshotButtonClicked()
    {
        // Mode 2 — повна сцена
        SaveManager.Instance.SaveFullScene("full_1", captureScreenshot: true);
    }

    public void OnLoadButtonClicked()
    {
        SaveManager.Instance.Load("slot_1");
    }

    public void OnRollbackClicked()
    {
        SaveManager.Instance.Rollback("slot_1", stepsBack: 1);
    }
}

// ============================================================
// Програмне налаштування авто-збереження (без Inspector-конфіга)
// ============================================================
public class GameBootstrap : MonoBehaviour
{
    private void Start()
    {
        var cfg = ScriptableObject.CreateInstance<AutoSaveConfig>();
        cfg.Mode                 = AutoSaveMode.Hybrid;
        cfg.IntervalSeconds      = 120f;
        cfg.SlotId               = "autosave";
        cfg.SaveMode             = SaveMode.State;
        cfg.CaptureScreenshot    = false;
        cfg.SkipIfNothingChanged = true;
        SaveManager.Instance.StartAutoSave(cfg);
    }

    private void OnCheckpointReached()
    {
        SaveManager.Instance.TriggerAutoSave(); // тільки якщо Mode = OnTrigger / Hybrid
    }
}
