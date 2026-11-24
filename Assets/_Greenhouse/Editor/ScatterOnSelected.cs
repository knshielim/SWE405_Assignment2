using UnityEditor;
using UnityEngine;

public class ScatterOnSelected : EditorWindow
{
    GameObject prefab;
    Transform parent;
    int perObject = 3;
    float minScale = 0.8f, maxScale = 1.2f;
    bool onlyFacingCamera = true;

    [MenuItem("Tools/Greenhouse/Scatter On Selected")]
    static void Open() => GetWindow<ScatterOnSelected>("Scatter");

    void OnSelectionChange() => Repaint();

    void OnGUI()
    {
        prefab   = (GameObject)EditorGUILayout.ObjectField("Prefab", prefab, typeof(GameObject), false);
        parent   = (Transform)EditorGUILayout.ObjectField("Parent", parent, typeof(Transform), true);
        perObject = EditorGUILayout.IntSlider("Per object", perObject, 1, 10);
        minScale = EditorGUILayout.FloatField("Min scale", minScale);
        maxScale = EditorGUILayout.FloatField("Max scale", maxScale);
        onlyFacingCamera = EditorGUILayout.Toggle("Only sides facing camera", onlyFacingCamera);

        using (new EditorGUI.DisabledScope(prefab == null || Selection.gameObjects.Length == 0))
            if (GUILayout.Button($"Scatter on {Selection.gameObjects.Length} selected")) Scatter();
    }

    void Scatter()
    {
        var cam = SceneView.lastActiveSceneView ? SceneView.lastActiveSceneView.camera : null;
        Undo.SetCurrentGroupName("Scatter prefabs");
        int group = Undo.GetCurrentGroup();

        foreach (var target in Selection.gameObjects)
        {
            var mf = target.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            var verts = mf.sharedMesh.vertices;
            var tris = mf.sharedMesh.triangles;
            var tr = target.transform;

            int placed = 0, tries = 0;
            while (placed < perObject && tries++ < 300)
            {
                int t = Random.Range(0, tris.Length / 3) * 3;
                Vector3 a = tr.TransformPoint(verts[tris[t]]);
                Vector3 b = tr.TransformPoint(verts[tris[t + 1]]);
                Vector3 c = tr.TransformPoint(verts[tris[t + 2]]);
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                Vector3 centre = (a + b + c) / 3f;

                if (onlyFacingCamera && cam &&
                    Vector3.Dot(n, (cam.transform.position - centre).normalized) < 0.3f) continue;

                float r1 = Random.value, r2 = Random.value;
                if (r1 + r2 > 1f) { r1 = 1f - r1; r2 = 1f - r2; }
                Vector3 p = a + r1 * (b - a) + r2 * (c - a);

                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(go, "Scatter");
                if (parent) go.transform.SetParent(parent, true);
                go.transform.position = p;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n)
                                        * Quaternion.Euler(0, Random.Range(0f, 360f), 0);
                go.transform.localScale = prefab.transform.localScale * Random.Range(minScale, maxScale);
                placed++;
            }
        }
        Undo.CollapseUndoOperations(group);
    }
}