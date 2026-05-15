using System.Collections.Generic;
using MessagePack;

namespace BitSaveSystem
{
    /// <summary>
    /// Повний знімок сцени. Кожен GameObject описаний як SceneObject —
    /// або з посиланням на префаб (компактно, безпечно), або з повним
    /// списком компонентів через рефлексію (fallback для динамічних об'єктів).
    /// </summary>
    [MessagePackObject]
    public sealed class SceneSnapshot
    {
        [Key(0)] public string SceneName { get; set; }
        [Key(1)] public List<SceneObject> Objects { get; set; } = new();
    }

    [MessagePackObject]
    public sealed class SceneObject
    {
        /// <summary>Стабільний GUID, що зберігається у компоненті SceneObjectId на рантаймі.</summary>
        [Key(0)] public string ObjectGuid { get; set; }

        [Key(1)] public string Name        { get; set; }
        [Key(2)] public bool   ActiveSelf  { get; set; }
        [Key(3)] public string ParentGuid  { get; set; } // null якщо root
        [Key(4)] public int    Layer       { get; set; }
        [Key(5)] public string Tag         { get; set; }

        // ---------- transform ----------
        [Key(6)]  public float[] LocalPosition { get; set; } // [x,y,z]
        [Key(7)]  public float[] LocalRotation { get; set; } // [x,y,z,w]
        [Key(8)]  public float[] LocalScale    { get; set; } // [x,y,z]

        // ---------- prefab path ----------
        /// <summary>GUID префаба з PrefabRegistry. null якщо об'єкт створено динамічно.</summary>
        [Key(9)]  public string PrefabGuid  { get; set; }

        /// <summary>Mode 2 з префабом: дельта змінених полів компонентів.</summary>
        [Key(10)] public List<ComponentDelta> ComponentDeltas { get; set; }

        // ---------- reflection fallback ----------
        /// <summary>Повний список компонентів (без префаба).</summary>
        [Key(11)] public List<ComponentSnapshot> Components { get; set; }

        /// <summary>SaveTable від ISaveable-компонентів цього GameObject.</summary>
        [Key(12)] public List<SaveTable> SaveableTables { get; set; }
    }

    /// <summary>Дельта одного компонента — лише поля, що відрізняються від префаба.</summary>
    [MessagePackObject]
    public sealed class ComponentDelta
    {
        [Key(0)] public string TypeName  { get; set; } // AssemblyQualifiedName-like
        [Key(1)] public int    Index     { get; set; } // індекс серед компонентів цього типу
        [Key(2)] public Dictionary<string, byte[]> ChangedFields { get; set; } = new();
    }

    /// <summary>Повний знімок компонента (рефлексивний fallback).</summary>
    [MessagePackObject]
    public sealed class ComponentSnapshot
    {
        [Key(0)] public string TypeName { get; set; }
        [Key(1)] public Dictionary<string, byte[]> Fields { get; set; } = new();
    }
}
