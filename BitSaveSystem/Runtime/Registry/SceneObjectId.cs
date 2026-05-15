using System;
using UnityEngine;

namespace BitSaveSystem
{
    /// <summary>
    /// Стабільний GUID для GameObject. Призначається в Editor (OnValidate) для
    /// сценних об'єктів і генерується на льоту при <c>Instantiate</c> для
    /// динамічно створених. Використовується SceneCapture/SceneRestore
    /// для пошуку існуючих об'єктів і відновлення зв'язків parent→child.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
    public sealed class SceneObjectId : MonoBehaviour
    {
        [SerializeField] private string _guid;

        public string Guid => string.IsNullOrEmpty(_guid) ? (_guid = System.Guid.NewGuid().ToString("N")) : _guid;

        public void AssignNewGuid() => _guid = System.Guid.NewGuid().ToString("N");

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Якщо це префаб у проекті (не інстанс у сцені) — не чіпаємо.
            if (UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this)) return;

            if (string.IsNullOrEmpty(_guid))
            {
                _guid = System.Guid.NewGuid().ToString("N");
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}
