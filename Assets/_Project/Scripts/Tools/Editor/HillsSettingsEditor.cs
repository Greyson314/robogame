using UnityEditor;
using UnityEngine;

namespace Robogame.Tools.Editor
{
    /// <summary>
    /// Adds a "Rebake hills mesh" button to the <see cref="HillsSettings"/>
    /// inspector so iteration doesn't require running the full
    /// <c>Build All Pass A</c> menu. The button just calls
    /// <see cref="HillsGround.RebakeMesh"/>, which overwrites the
    /// existing mesh asset in place — every scene that references the
    /// mesh updates immediately.
    /// </summary>
    [CustomEditor(typeof(HillsSettings))]
    public sealed class HillsSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button("Rebake hills mesh", GUILayout.Height(28)))
                {
                    HillsGround.RebakeMesh();
                }
            }
            EditorGUILayout.HelpBox(
                "Rebakes Mesh_ArenaHills.asset in place. Every Ground GameObject " +
                "that references it (e.g. the open Arena scene) updates immediately. " +
                "No need to re-run Build Arena Pass A.",
                MessageType.None);
        }
    }
}
