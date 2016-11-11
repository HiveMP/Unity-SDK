using UnityEditor;
using UnityEngine;

namespace Assets.HiveMP.Editor
{
    [CustomEditor(typeof(HiveComponent))]
    public class HiveComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (EditorApplication.isPlaying)
            {
                foreach (var target in targets)
                {
                    var component = (HiveComponent) target;
                    var temporarySession = component.GetTempSessionWithSecrets();

                    EditorGUILayout.LabelField("Current Temporary Session", EditorStyles.boldLabel);
                    if (temporarySession == null)
                    {
                        EditorGUILayout.LabelField("There is no current temporary session");
                    }
                    else
                    {
                        EditorGUILayout.LabelField(
                            "Session ID",
                            temporarySession.Id);
                        EditorGUILayout.LabelField(
                            "API Key",
                            temporarySession.ApiKey);
                        EditorGUILayout.LabelField(
                            "Secret Key",
                            temporarySession.SecretKey);
                        EditorGUILayout.LabelField(
                            "Expiry",
                            ((int)(temporarySession.Expiry ?? 0)).ToString());
                    }
                }

                this.Repaint();
            }
        }
    }
}
