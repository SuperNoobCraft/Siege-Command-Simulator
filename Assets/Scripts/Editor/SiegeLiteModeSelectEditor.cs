using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SiegeLiteModeSelect))]
public class SiegeLiteModeSelectEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Stand Circles", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Generate three discs, then move them in the Scene view where players should stand.",
            MessageType.Info);

        SiegeLiteModeSelect select = (SiegeLiteModeSelect)target;

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Generate Stand Circles", GUILayout.Height(28f)))
            {
                select.GenerateStandCircles(replaceExisting: false);
            }

            if (GUILayout.Button("Generate Stand Circles (Replace Existing)"))
            {
                if (EditorUtility.DisplayDialog(
                        "Replace Lite Stand Circles?",
                        "Delete existing Timed / Endless / Main Menu discs and create new ones?",
                        "Replace",
                        "Cancel"))
                {
                    select.GenerateStandCircles(replaceExisting: true);
                }
            }
        }
    }
}
