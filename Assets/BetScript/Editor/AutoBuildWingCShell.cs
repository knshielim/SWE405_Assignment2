// AutoBuildWingCShell.cs
// Taruh di folder Editor, misalnya:  Assets/BetScript/Editor/AutoBuildWingCShell.cs
// Butuh: Unity 2021+, ProBuilder 5.x/6.x, Windows (untuk gerak kursor asli),
//        dan material dari Tools > Greenhouse > Create Wing C Materials.
//
// Buka:        Tools > Auto Build > Wing C Shell
// Stop darurat: Ctrl+Shift+Alt+Q  (atau tombol STOP di window)
//
// Rekonstruksi VISUAL tutorial manual: buat contoh, duplicate (Ctrl+D),
// sesuaikan ukuran/posisi, lanjut copy objek sejenis, cek dan rapikan.
// Kesalahan kecil terjadi sesekali, sesuai konteks, lalu diperbaiki.
// Tidak ada caption tahap di Scene View; narasi bisa ditambahkan lewat voice-over.
// PERHATIAN: geometri diubah melalui Editor API, bukan klik/ketik Unity sungguhan.
// Hasil akhir: WingC_Shell dengan Floor, Wall_North/South/East/West, Roof, Sign_WingC.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.ProBuilder;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;
using PBMath = UnityEngine.ProBuilder.Math;

public class AutoBuildWingCShell : EditorWindow
{
    const string ScenePath = "Assets/_Greenhouse/WingC_Nursery/Scenes/WingC_Work.unity";
    const string MatFolder = "Assets/_Greenhouse/WingC_Nursery/Materials";
    const float RoofAngle = 26.565f;   // atan(2 / 4): naik 2 m dalam 4 m
    const float RafterLen = 4.4721f;   // sqrt(4^2 + 2^2)

    // ---------------- Pengaturan ----------------
    static float speed = 1f;            // 1 = tempo pemula, < 1 lebih lambat, > 1 lebih cepat
    static float mistakeRate = 0.4f;    // 0 = tanpa salah, 1 = masih hanya beberapa koreksi alami
    static int seed = 7;                // ganti angka ini untuk variasi kesalahan yang berbeda
    static bool moveRealCursor = true;
    static Vector2 cursorOffset = Vector2.zero;

    // ---------------- Runner ----------------
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static double waitUntil;
    static bool Running => stack.Count > 0;
    static double Now => EditorApplication.timeSinceStartup;

    static bool cursorOn;
    static Vector3 cursorWorld;
    static ProBuilderMesh cur;
    static System.Random rng = new System.Random(7);

    static Material mFrame, mGlass, mFloor, mSoil;
    static readonly Dictionary<string, Transform> G = new Dictionary<string, Transform>();
    static readonly Dictionary<GameObject, Material> pendingMat = new Dictionary<GameObject, Material>();
    static readonly Dictionary<GameObject, string> pendingName = new Dictionary<GameObject, string>();
    // Benda contoh yang dibuat manual pertama kali, dipakai ulang ketika sejenis.
    static readonly Dictionary<string, GameObject> lastBoxInFamily = new Dictionary<string, GameObject>();
    static int correctionCooldown;
    static int duplicateStreak;

    static readonly Vector3 R = Vector3.right, L = Vector3.left, U = Vector3.up,
                            D = Vector3.down, N = Vector3.forward, S = Vector3.back;

#if UNITY_EDITOR_WIN
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetCursorPos(int x, int y);
#else
    static bool SetCursorPos(int x, int y) { return false; }
#endif

    // ================= UI =================
    [MenuItem("Tools/Auto Build/Wing C Shell")]
    static void Open() => GetWindow<AutoBuildWingCShell>("Wing C Build");

    [MenuItem("Tools/Auto Build/Stop Wing C %#&q")]
    static void StopMenu() => Stop();

    void OnGUI()
    {
        EditorGUILayout.LabelField("Wing C — Greenhouse Shell", EditorStyles.boldLabel);
        speed = EditorGUILayout.Slider("Kecepatan", speed, 0.1f, 4f);
        mistakeRate = EditorGUILayout.Slider("Tingkat salah", mistakeRate, 0f, 1f);
        seed = EditorGUILayout.IntField("Seed (variasi)", seed);
        moveRealCursor = EditorGUILayout.Toggle("Gerakkan kursor asli", moveRealCursor);
        cursorOffset = EditorGUILayout.Vector2Field("Koreksi kursor (px)", cursorOffset);

        GUILayout.Space(6);
        if (GUILayout.Button(Running ? "STOP" : "Mulai bangun Wing C", GUILayout.Height(32)))
        {
            if (Running) Stop(); else StartBuild();
        }
        if (GUILayout.Button("Tes kursor (ke titik 10,0,0 selama 3 detik)"))
            Begin(TestCursor());

        EditorGUILayout.HelpBox(
            "Rekonstruksi visual proses modeling (bukan klik manual sungguhan).\n" +
            "Mulai rekaman layar, biarkan Scene View tetap berukuran sama.\n" +
            "Satu objek dibuat lebih dulu, bagian serupa di-copy lalu disesuaikan.\n" +
            "Tidak ada caption tutorial: gunakan voice-over sendiri.\n" +
            "Stop darurat: Ctrl+Shift+Alt+Q.", MessageType.Info);
    }

    void Update() { if (Running) Repaint(); }

    // ================= Start =================
    static void StartBuild()
    {
        if (!EnsureScene()) return;
        if (!LoadMaterials()) return;
        if (SceneView.lastActiveSceneView == null) GetWindow<SceneView>();

        var old = GameObject.Find("WingC_Shell");
        if (old != null)
        {
            if (!UnityEditor.EditorUtility.DisplayDialog("Wing C", "WingC_Shell sudah ada di scene. Hapus dan bangun ulang?", "Hapus & bangun", "Batal"))
                return;
            Undo.DestroyObjectImmediate(old);
        }

        rng = new System.Random(seed);
        G.Clear(); pendingMat.Clear(); pendingName.Clear(); lastBoxInFamily.Clear();
        correctionCooldown = 0;
        duplicateStreak = 0;
        Begin(BuildWingC());
    }

    static bool EnsureScene()
    {
        if (EditorSceneManager.GetActiveScene().path == ScenePath) return true;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            return UnityEditor.EditorUtility.DisplayDialog("Wing C",
                $"Scene tidak ditemukan:\n{ScenePath}\n\nBangun di scene yang sedang terbuka?", "Ya", "Batal");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        return true;
    }

    static Material LoadMat(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{n}.mat");

    static bool LoadMaterials()
    {
        mFrame = LoadMat("M_Frame"); mGlass = LoadMat("M_Glass");
        mFloor = LoadMat("M_Floor"); mSoil = LoadMat("M_Soil");
        if (mFrame && mGlass && mFloor && mSoil) return true;
        UnityEditor.EditorUtility.DisplayDialog("Wing C",
            "Material Wing C belum ada.\nJalankan dulu Tools > Greenhouse > Create Wing C Materials.", "OK");
        return false;
    }

    // ================= Runner =================
    static void Begin(IEnumerator routine)
    {
        Stop();
        stack.Push(routine);
        EditorApplication.update += Tick;
        SceneView.duringSceneGui += OnSceneGUI;
    }

    static void Stop()
    {
        stack.Clear();
        cursorOn = false;
        EditorApplication.update -= Tick;
        SceneView.duringSceneGui -= OnSceneGUI;
    }

    static void Tick()
    {
        if (stack.Count == 0) { Stop(); return; }
        SceneView.RepaintAll();
        if (Now < waitUntil) return;

        var top = stack.Peek();
        bool moved;
        try { moved = top.MoveNext(); }
        catch (Exception e) { Debug.LogException(e); Stop(); return; }

        if (!moved) { stack.Pop(); if (stack.Count == 0) Stop(); return; }

        var c = top.Current;
        if (c is IEnumerator child) stack.Push(child);
        else if (c is float sec) waitUntil = Now + sec / speed;
    }

    static void OnSceneGUI(SceneView sv)
    {
        if (!cursorOn || !moveRealCursor) return;
        if (Event.current.type != EventType.Repaint) return;
        if (sv != SceneView.lastActiveSceneView) return;

        Vector2 gui = UnityEditor.HandleUtility.WorldToGUIPoint(cursorWorld);
        Vector2 scr = GUIUtility.GUIToScreenPoint(gui) * EditorGUIUtility.pixelsPerPoint + cursorOffset;
        SetCursorPos(Mathf.RoundToInt(scr.x), Mathf.RoundToInt(scr.y));
    }

    // ================= Helper umum =================
    static float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    // Salah kecil tak selalu muncul; jeda beberapa aksi supaya tidak tampak seperti
    // script yang sengaja membuat kesalahan pada tiap objek.
    static bool HumanSlip(float weight = 1f)
    {
        if (correctionCooldown > 0) { correctionCooldown--; return false; }
        if (rng.NextDouble() >= mistakeRate * 0.24f * weight) return false;
        correctionCooldown = rng.Next(5, 10);
        return true;
    }

    // Jeda alami yang pendek: fokus lebih lama untuk langkah baru daripada duplikat.
    static float Think(float a = 0.7f, float b = 1.8f)
        => rng.NextDouble() < 0.07 ? Rand(2.8f, 5.2f) : Rand(a, b);

    static string BoxFamily(string name)
    {
        if (name.StartsWith("KickWall")) return "kickwall";
        if (name.StartsWith("EaveBeam")) return "eavebeam";
        if (name.StartsWith("TransomBar")) return "transom";
        if (name.StartsWith("GlassPane")) return "wallglass";
        if (name.StartsWith("Purlin")) return "purlin";
        if (name.StartsWith("Post_") || name.StartsWith("DoorJamb")) return "post";
        return name; // geometri khusus dimulai dari kubus baru
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);
    static SceneView SV => SceneView.lastActiveSceneView;

    static Vector3 Center(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        return r != null ? r.bounds.center : go.transform.position;
    }

    static IEnumerator Tween(float duration, Action<float> step)
    {
        double start = Now;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.Clamp01((float)((Now - start) * speed / duration));
            step(Smooth(t));
            yield return null;
        }
    }

    static IEnumerator MoveCursor(Vector3 target, float duration = -1f)
    {
        if (duration < 0f) duration = Rand(0.7f, 1.3f);
        cursorOn = true;
        Vector3 from = cursorWorld;
        var sv = SV;
        Vector3 up = sv != null ? sv.camera.transform.up : Vector3.up;
        float arc = Vector3.Distance(from, target) * 0.12f * (rng.NextDouble() < 0.5 ? -1f : 1f);
        yield return Tween(duration, t =>
            cursorWorld = Vector3.Lerp(from, target, t) + up * Mathf.Sin(t * Mathf.PI) * arc);
        yield return 0.15f;
    }

    // ================= Kamera =================
    static IEnumerator CamTo(Vector3 pivot, float yaw, float pitch, float size, float dur)
    {
        var sv = SV; if (sv == null) yield break;
        Vector3 p0 = sv.pivot; Quaternion r0 = sv.rotation; float s0 = sv.size;
        Quaternion r1 = Quaternion.Euler(pitch, yaw, 0f);
        yield return Tween(dur, t =>
            sv.LookAtDirect(Vector3.Lerp(p0, pivot, t), Quaternion.Slerp(r0, r1, t), Mathf.Lerp(s0, size, t)));
    }

    // seperti tekan F pada objek
    static IEnumerator Focus(Vector3 pivot, float size)
    {
        var sv = SV; if (sv == null) yield break;
        Vector3 e = sv.rotation.eulerAngles;
        yield return CamTo(pivot, e.y, e.x, size, Rand(0.7f, 1.2f));
    }

    // orbit kiri-kanan untuk mengecek hasil
    static IEnumerator LookAround(Vector3 pivot, float size)
    {
        var sv = SV; if (sv == null) yield break;
        float yaw = sv.rotation.eulerAngles.y, pitch = sv.rotation.eulerAngles.x;
        yield return CamTo(pivot, yaw + Rand(25f, 45f), pitch + Rand(-4f, 8f), size, Rand(1.5f, 2.5f));
        yield return Rand(0.8f, 1.8f);
        yield return CamTo(pivot, yaw - Rand(10f, 30f), pitch, size, Rand(1.8f, 2.8f));
        yield return Rand(0.8f, 1.5f);
        yield return CamTo(pivot, yaw, pitch, size, Rand(1.2f, 2f));
    }

    static IEnumerator Orbit(Vector3 pivot, float size, float duration)
    {
        var sv = SV; if (sv == null) yield break;
        yield return CamTo(pivot, sv.rotation.eulerAngles.y, 28f, size, 1.5f);
        float yaw0 = sv.rotation.eulerAngles.y;
        bool c = cursorOn; cursorOn = false;
        yield return Tween(duration, t => sv.LookAtDirect(pivot, Quaternion.Euler(28f, yaw0 + 360f * t, 0f), size));
        cursorOn = c;
    }

    // ================= Helper ProBuilder =================
    static Vector3 CenterLocal(ProBuilderMesh pb, IEnumerable<Face> faces)
    {
        var pos = pb.positions;
        var idx = faces.SelectMany(f => f.distinctIndexes).Distinct().ToList();
        Vector3 sum = Vector3.zero;
        foreach (var i in idx) sum += pos[i];
        return sum / Mathf.Max(1, idx.Count);
    }

    static Vector3 CenterWorld(ProBuilderMesh pb, IEnumerable<Face> faces)
        => pb.transform.TransformPoint(CenterLocal(pb, faces));

    static List<Face> FacesToward(ProBuilderMesh pb, Vector3 dir)
    {
        var cand = pb.faces.Where(f => Vector3.Dot(PBMath.Normal(pb, f), dir) > 0.99f).ToList();
        if (cand.Count == 0) return cand;
        float max = cand.Max(f => Vector3.Dot(CenterLocal(pb, new[] { f }), dir));
        return cand.Where(f => Mathf.Abs(Vector3.Dot(CenterLocal(pb, new[] { f }), dir) - max) < 0.001f).ToList();
    }

    static void RefreshPB() { try { ProBuilderEditor.Refresh(); } catch { } }

    static void HighlightFaces(ProBuilderMesh pb, IEnumerable<Face> faces)
    {
        try
        {
            ProBuilderEditor.selectMode = SelectMode.Face;
            Selection.activeGameObject = pb.gameObject;
            pb.SetSelectedFaces(faces);
            ProBuilderEditor.Refresh();
        }
        catch { Selection.activeGameObject = pb.gameObject; }
    }

    static void SelectObject(GameObject go)
    {
        // ProBuilder 6 tidak punya SelectMode.Object; kalau tidak ada, biarkan mode sekarang
        try { if (Enum.TryParse("Object", out SelectMode objMode)) ProBuilderEditor.selectMode = objMode; } catch { }
        Selection.activeGameObject = go;
    }

    static Vector3 Axis(Vector3 dir) => new Vector3(Mathf.Abs(dir.x), Mathf.Abs(dir.y), Mathf.Abs(dir.z));

    static float DistTo(ProBuilderMesh pb, List<Face> faces, Vector3 dir, float target)
    {
        Vector3 ax = Axis(dir);
        return (target - Vector3.Dot(CenterWorld(pb, faces), ax)) * Vector3.Dot(dir, ax);
    }

    static IEnumerator Push(ProBuilderMesh pb, List<Face> faces, Vector3 dir, float dist)
    {
        var idx = faces.SelectMany(f => f.distinctIndexes).Distinct().ToArray();
        float applied = 0f;
        float duration = Mathf.Clamp(0.7f + Mathf.Abs(dist) * 0.3f, 0.7f, 3f);

        yield return Tween(duration, t =>
        {
            float want = dist * t;
            pb.TranslateVertices(idx, dir * (want - applied));
            applied = want;
            pb.ToMesh(); pb.Refresh();
            cursorWorld = CenterWorld(pb, faces);
            RefreshPB();
        });
    }

    // ================= Aksi "pemula" =================

    static IEnumerator CreateGroup(string name, string parentKey)
    {
        yield return Think(0.8f, 1.8f);                 // klik kanan > Create Empty
        var go = new GameObject("GameObject");
        Undo.RegisterCreatedObjectUndo(go, "Create Empty");
        if (parentKey != null) go.transform.SetParent(G[parentKey], false);
        Selection.activeGameObject = go;
        yield return Rand(1.2f, 2.4f);                  // ketik nama
        go.name = name;
        G[name] = go.transform;
        yield return Rand(0.3f, 0.6f);
    }

    static IEnumerator Spawn(string finalName, Vector3 pos, Transform parent)
    {
        duplicateStreak = 0; // jenis bentuk baru: berpikir lebih lama
        yield return Think();
        bool off = HumanSlip(0.45f);                     // sesekali salah taruh cube pertama
        Vector3 at = off ? pos + new Vector3(Rand(-0.25f, 0.25f), 0f, Rand(-0.25f, 0.25f)) : pos;
        yield return MoveCursor(at);

        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, Vector3.one);
        var go = pb.gameObject;
        go.name = "Cube";
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        Undo.RegisterCreatedObjectUndo(go, "Auto Build Wing C");
        cur = pb;
        SelectObject(go);

        go.transform.localScale = Vector3.one * 0.01f;
        yield return Tween(0.35f, t => go.transform.localScale = Vector3.one * Mathf.Max(0.01f, t));
        go.transform.localScale = Vector3.one;
        yield return Rand(0.4f, 1f);

        if (off) { yield return Think(1f, 2f); yield return MoveObject(go.transform, pos); }
        yield return Rename(go, finalName);
    }

    static IEnumerator Rename(GameObject go, string finalName)
    {
        if (HumanSlip(0.8f)) { pendingName[go] = finalName; yield break; }  // rename nanti
        yield return Rand(1.2f, 2.8f);                                    // klik Hierarchy, ketik nama
        Undo.RecordObject(go, "Rename");
        go.name = finalName;
        yield return Rand(0.3f, 0.6f);
    }

    static void ApplyMat(GameObject go, Material m)
    {
        var r = go.GetComponent<MeshRenderer>();
        if (r == null) return;
        Undo.RecordObject(r, "Material");
        r.sharedMaterial = m;
    }

    static IEnumerator SetMat(GameObject go, Material m)
    {
        // Kadang material dipasang belakangan sekaligus untuk beberapa object sejenis;
        // bukan sengaja memilih material salah lalu menggantinya berulang-ulang.
        if (rng.NextDouble() < 0.10 + mistakeRate * 0.05)
        {
            pendingMat[go] = m;
            yield break;
        }
        yield return Think(0.4f, 1.1f);
        yield return MoveCursor(Center(go), Rand(0.35f, 0.65f));
        ApplyMat(go, m);
        yield return Rand(0.2f, 0.5f);
    }

    static IEnumerator MoveObject(Transform tr, Vector3 target)
    {
        yield return MoveCursor(Center(tr.gameObject));
        Vector3 from = tr.position;
        Vector3 off = Center(tr.gameObject) - tr.position;
        float dist = Vector3.Distance(from, target);
        yield return Tween(Mathf.Clamp(0.6f + dist * 0.18f, 0.6f, 3f), t =>
        {
            tr.position = Vector3.Lerp(from, target, t);
            cursorWorld = tr.position + off;
        });
        tr.position = target;
        yield return Rand(0.2f, 0.5f);
    }

    static IEnumerator RotateTo(Transform tr, Vector3 euler)
    {
        yield return Think(0.7f, 1.5f);
        yield return MoveCursor(Center(tr.gameObject));
        Quaternion a = tr.rotation;

        if (HumanSlip(0.7f) && Mathf.Abs(euler.x) > 1f)
        {
            // Rotasi mendekati nilai target; lihat kemiringan lalu rapikan angkanya.
            Vector3 near = euler + new Vector3(Rand(-2.0f, 2.0f), 0f, 0f);
            Quaternion guess = Quaternion.Euler(near);
            yield return Tween(Rand(0.8f, 1.2f), t => tr.rotation = Quaternion.Slerp(a, guess, t));
            a = guess;
            yield return Think(0.8f, 1.6f);
        }

        Quaternion b = Quaternion.Euler(euler);
        yield return Tween(Rand(0.8f, 1.5f), t => tr.rotation = Quaternion.Slerp(a, b, t));
        tr.rotation = b;
        yield return Rand(0.2f, 0.45f);
    }

    static IEnumerator Reparent(GameObject go, Transform parent)
    {
        yield return Rand(1.2f, 2.5f);                  // drag di Hierarchy
        Undo.SetTransformParent(go.transform, parent, "Reparent");
        yield return Rand(0.3f, 0.6f);
    }

    // Ctrl+D lalu geser sejauh 'delta' (posisi dunia)
    static IEnumerator Duplicate(GameObject src, string finalName, Vector3 delta,
                                 Transform newParent = null, Vector3? euler = null)
    {
        var sv = SV;
        Vector3 tgtPreview = src.transform.position + delta;
        if (sv != null && Vector3.Distance(sv.pivot, tgtPreview) > sv.size * 0.9f)
            yield return Focus((Center(src) + tgtPreview) * 0.5f, Mathf.Max(sv.size, delta.magnitude * 0.8f + 2f));

        // Setelah beberapa copy berurutan, tempo kerja secara alami meningkat.
        yield return duplicateStreak >= 3 ? Think(0.35f, 0.9f) : Think(0.8f, 1.7f);
        duplicateStreak++;
        yield return MoveCursor(Center(src));
        SelectObject(src);
        yield return Rand(0.3f, 0.7f);                  // Ctrl+D

        GameObject copy = null;
        try { Unsupported.DuplicateGameObjectsUsingPasteboard(); copy = Selection.activeGameObject; } catch { }
        if (copy == null || copy == src)
        {
            copy = (GameObject)UnityEngine.Object.Instantiate(src, src.transform.position, src.transform.rotation, src.transform.parent);
            copy.name = src.name + " (1)";
            Undo.RegisterCreatedObjectUndo(copy, "Duplicate");
            Selection.activeGameObject = copy;
        }
        // Copy ProBuilder harus punya Mesh sendiri supaya face yang di-adjust
        // tidak ikut mengubah mesh benda contoh yang masih dipakai sebagai acuan.
        var srcFilter = src.GetComponent<MeshFilter>();
        var copyFilter = copy.GetComponent<MeshFilter>();
        if (srcFilter != null && copyFilter != null && srcFilter.sharedMesh != null
            && copyFilter.sharedMesh == srcFilter.sharedMesh)
        {
            copyFilter.sharedMesh = UnityEngine.Object.Instantiate(srcFilter.sharedMesh);
            copyFilter.sharedMesh.name = srcFilter.sharedMesh.name + "_Copy";
        }
        cur = copy.GetComponent<ProBuilderMesh>();
        if (pendingMat.TryGetValue(src, out var pm)) pendingMat[copy] = pm;
        yield return Rand(0.3f, 0.6f);

        Vector3 target = copy.transform.position + delta;
        if (HumanSlip(0.9f) && delta.sqrMagnitude > 0.1f)
        {
            // Copy sudah di tempat hampir benar, tapi snap/angka meleset sedikit.
            int axis = Mathf.Abs(delta.x) > 0.01f ? 0 : (Mathf.Abs(delta.z) > 0.01f ? 2 : 1);
            Vector3 nudge = axis == 0 ? Vector3.right : (axis == 2 ? Vector3.forward : Vector3.up);
            Vector3 near = target + nudge * Rand(-0.16f, 0.16f);
            yield return MoveObject(copy.transform, near);
            yield return Think(0.8f, 1.5f); // membandingkan dengan objek sebelah
        }
        yield return MoveObject(copy.transform, target);

        if (euler.HasValue) yield return RotateTo(copy.transform, euler.Value);
        if (newParent != null) yield return Reparent(copy, newParent);
        yield return Rename(copy, finalName);
    }

    // Menarik face ke koordinat target. Kesalahan yang realistis: kurang/lebih
    // beberapa sentimeter, lalu diperbaiki setelah cek garis dengan objek sebelah.
    static IEnumerator DragFace(Vector3 dir, float target, bool fromCopy = false)
    {
        var pb = cur;
        if (pb == null) yield break;
        var faces = FacesToward(pb, dir);
        if (faces.Count == 0) yield break;
        float dist = DistTo(pb, faces, dir, target);
        if (Mathf.Abs(dist) < 0.004f) yield break; // jangan mengedit face yang sudah benar

        yield return Think(fromCopy ? 0.2f : 0.55f, fromCopy ? 0.7f : 1.3f);
        yield return MoveCursor(CenterWorld(pb, faces), fromCopy ? Rand(0.3f, 0.65f) : -1f);
        HighlightFaces(pb, faces);
        yield return Rand(0.15f, 0.35f);

        if (HumanSlip(fromCopy ? 0.9f : 0.7f))
        {
            // Jangan melewati face lawan, terutama pada kaca tipis 2 cm.
            var opposite = FacesToward(pb, -dir);
            float safeSpan = opposite.Count == 0 ? 0.05f
                : Vector3.Dot(CenterWorld(pb, faces) + dir * dist
                    - CenterWorld(pb, opposite), dir);
            float absSlip = Mathf.Min(Rand(0.035f, 0.12f), Mathf.Abs(dist) * 0.22f,
                Mathf.Max(0.001f, safeSpan * 0.2f));
            float amount = absSlip * (rng.NextDouble() < 0.5 ? -1f : 1f);
            yield return Push(pb, faces, dir, dist + amount);
            yield return Think(0.6f, 1.2f);
            yield return Push(pb, faces, dir, -amount);
        }
        else yield return Push(pb, faces, dir, dist);
        yield return Rand(0.1f, 0.3f);
    }

    // Keenam face dicek setelah copy, tapi hanya face yang dimensinya perlu berubah
    // yang benar-benar digeser. Posisi baru sudah diatur lewat Duplicate().
    static IEnumerator FitCopiedBox(Vector3 min, Vector3 max)
    {
        yield return DragFace(U, max.y, true); yield return DragFace(D, min.y, true);
        yield return DragFace(R, max.x, true); yield return DragFace(L, min.x, true);
        yield return DragFace(N, max.z, true); yield return DragFace(S, min.z, true);
    }

    // Satu balok contoh dibuat dari Cube; untuk balok berikutnya gunakan
    // Ctrl+D dan sesuaikan secara visual. Bangun baru hanya jika jenisnya baru.
    static IEnumerator BuildBox(string name, Transform parent, Vector3 min, Vector3 max, Material m)
    {
        Vector3 c = (min + max) * 0.5f, size = max - min;
        string family = BoxFamily(name);
        GameObject template;
        bool copied = lastBoxInFamily.TryGetValue(family, out template)
            && template != null && template.GetComponent<ProBuilderMesh>() != null
            && Vector3.Distance(template.transform.localScale, Vector3.one) < 0.001f
            && Quaternion.Angle(template.transform.rotation, Quaternion.identity) < 0.01f;

        // Pemodelan manual: saat copy kecil-kecilan, kamera biasanya tetap.
        // Geser/fokus kamera hanya jika target jauh atau saat memulai jenis baru.
        if (!copied || SV == null || Vector3.Distance(SV.pivot, c) > SV.size * 0.65f)
            yield return Focus(c, Mathf.Clamp(Mathf.Max(size.x, size.y, size.z) * 0.6f, 2f, 8f));

        if (copied)
        {
            // Contoh manusia: pilih benda serupa -> Ctrl+D -> geser -> adjust.
            yield return Duplicate(template, name, c - template.transform.position, parent);
            yield return FitCopiedBox(min, max);
            if (cur != null && cur.gameObject != null)
            {
                var rend = cur.GetComponent<MeshRenderer>();
                if (rend == null || rend.sharedMaterial != m) yield return SetMat(cur.gameObject, m);
            }
        }
        else
        {
            yield return Spawn(name, c, parent);
            var go = cur.gameObject;
            yield return DragFace(U, max.y); yield return DragFace(D, min.y);
            yield return DragFace(R, max.x); yield return DragFace(L, min.x);
            yield return DragFace(N, max.z); yield return DragFace(S, min.z);
            yield return SetMat(go, m);
        }

        // Template terbaru, bukan sekadar semua balok baru dari nol.
        if (cur != null) lastBoxInFamily[family] = cur.gameObject;
    }

    // Face atas di-scale ke 0 pada sumbu Z -> jadi segitiga (gable)
    static IEnumerator CollapseTop(ProBuilderMesh pb)
    {
        yield return Think(1.5f, 3f);
        var faces = FacesToward(pb, U);
        if (faces.Count == 0) yield break;
        yield return MoveCursor(CenterWorld(pb, faces));
        HighlightFaces(pb, faces);
        yield return 0.3f;

        var pos = pb.positions;
        Vector3 c = CenterLocal(pb, faces);
        var idx = faces.SelectMany(f => f.distinctIndexes).Distinct()
            .GroupBy(i => Vector3Int.RoundToInt(pos[i] * 1000f)).Select(g => g.First()).ToArray();
        var start = idx.ToDictionary(i => i, i => pos[i]);
        var applied = idx.ToDictionary(i => i, i => Vector3.zero);
        Vector3 scale = new Vector3(1f, 1f, 0.001f);

        yield return Tween(Rand(1.4f, 2.2f), t =>
        {
            Vector3 s = Vector3.Lerp(Vector3.one, scale, t);
            foreach (var i in idx)
            {
                Vector3 want = (c + Vector3.Scale(start[i] - c, s)) - start[i];
                pb.TranslateVertices(new[] { i }, want - applied[i]);
                applied[i] = want;
            }
            pb.ToMesh(); pb.Refresh();
            RefreshPB();
        });
        yield return Rand(0.3f, 0.6f);
    }

    // Kemiringan atap dicoba sekali secara visual, lalu disesuaikan setelah
    // melihat pertemuan ujung kasau. Tidak perlu serangkaian angka yang ngawur.
    static IEnumerator EyeballRafter(ProBuilderMesh pb, Vector3 ridgePt, Vector3 eavePt)
    {
        var tr = pb.transform;
        yield return Think(1.5f, 2.8f);
        float firstAngle = RoofAngle + Rand(-2.8f, 2.8f);
        Quaternion start = tr.rotation;
        Quaternion near = Quaternion.Euler(firstAngle, 0f, 0f);
        yield return MoveCursor(Center(tr.gameObject));
        yield return Tween(Rand(1.0f, 1.8f), t => tr.rotation = Quaternion.Slerp(start, near, t));
        tr.rotation = near;
        yield return Focus(ridgePt, 1.8f);
        yield return Think(1.0f, 2.0f);
        yield return Focus(eavePt, 1.8f);
        yield return Think(0.8f, 1.7f);
        yield return Focus((ridgePt + eavePt) * 0.5f, 3.2f);
        Quaternion correct = Quaternion.Euler(RoofAngle, 0f, 0f);
        yield return Tween(Rand(0.6f, 1.1f), t => tr.rotation = Quaternion.Slerp(near, correct, t));
        tr.rotation = correct;
    }

    // Tarik face pada sumbu LOKAL (untuk objek yang sudah diputar), kira-kira dulu lalu dipaskan
    static IEnumerator DragLocal(ProBuilderMesh pb, Vector3 dirLocal, float target, Vector3 checkPt)
    {
        yield return Focus(checkPt, 1.5f);
        yield return Think(1f, 2.5f);                                 // "lho, kok nggak nyampe"
        var faces = FacesToward(pb, dirLocal);
        if (faces.Count == 0) yield break;
        yield return MoveCursor(CenterWorld(pb, faces));
        HighlightFaces(pb, faces);
        yield return Rand(0.2f, 0.4f);

        float now = Vector3.Dot(CenterLocal(pb, faces), dirLocal);
        float guess = target + (HumanSlip(0.85f) ? Rand(-0.13f, 0.13f) : 0f);
        yield return Push(pb, faces, dirLocal, guess - now);
        now = Vector3.Dot(CenterLocal(pb, faces), dirLocal);
        if (Mathf.Abs(target - now) > 0.004f)
        {
            yield return Think(0.8f, 1.5f);
            yield return Push(pb, faces, dirLocal, target - now);
        }
        yield return Think(0.4f, 0.9f);
    }

    // Akhir fase: orbit cek hasil, lalu benerin yang lupa (material, nama)
    static IEnumerator CheckPhase(Vector3 pivot, float size)
    {
        duplicateStreak = 0;
        Selection.activeGameObject = null;
        yield return Think(1f, 2f);
        yield return LookAround(pivot, size);

        if (pendingMat.Count > 0)
        {
            foreach (var grp in pendingMat.Where(kv => kv.Key != null).GroupBy(kv => kv.Value).ToList())
            {
                var gos = grp.Select(kv => kv.Key).ToArray();
                yield return Think(1.5f, 3f);           // "lho, kok masih abu-abu"
                yield return MoveCursor(Center(gos[0]));
                Selection.objects = gos;
                yield return Rand(0.8f, 1.6f) + 0.3f * gos.Length;   // ctrl+klik satu-satu
                foreach (var g in gos) ApplyMat(g, grp.Key);
                yield return Rand(0.5f, 1f);
            }
            pendingMat.Clear();
        }

        if (pendingName.Count > 0)
        {
            yield return Think(1f, 2f);                 // "namanya masih Cube semua"
            foreach (var kv in pendingName.Where(kv => kv.Key != null).ToList())
            {
                Selection.activeGameObject = kv.Key;
                yield return Rand(1.2f, 2.5f);
                Undo.RecordObject(kv.Key, "Rename");
                kv.Key.name = kv.Value;
            }
            pendingName.Clear();
        }
        Selection.activeGameObject = null;
    }

    static IEnumerator TestCursor()
    {
        cursorOn = true;
        cursorWorld = new Vector3(10f, 0f, 0f);
        yield return 3f;
        cursorOn = false;
    }

    // ================= Rencana bangunan: kerangka Wing C =================
    // Koordinat: X timur, Y atas, Z utara. Wing C: X 4..16, Z -4..4.
    static IEnumerator BuildWingC()
    {
        EditorApplication.ExecuteMenuItem("Tools/ProBuilder/ProBuilder Window");
        var sv = SV;
        if (sv != null) sv.LookAtDirect(new Vector3(10f, 1.2f, 0f), Quaternion.Euler(30f, 30f, 0f), 10f);
        cursorWorld = new Vector3(6f, 0f, -6f);
        cursorOn = true;
        yield return 1.5f;

        // ===== Fase 1 — Lantai =====
        yield return CreateGroup("WingC_Shell", null);
        yield return BuildBox("Floor", G["WingC_Shell"], new Vector3(4f, -0.08f, -4f), new Vector3(16f, 0.02f, 4f), mFloor);
        yield return CheckPhase(new Vector3(10f, 0.5f, 0f), 9f);

        // ===== Fase 2 — Dinding bawah =====
        foreach (var w in new[] { "Wall_North", "Wall_South", "Wall_East", "Wall_West" })
            yield return CreateGroup(w, "WingC_Shell");
        yield return BuildBox("KickWall", G["Wall_North"], new Vector3(4f, 0f, 3.95f), new Vector3(16f, 0.4f, 4.05f), mFrame);
        yield return Duplicate(cur.gameObject, "KickWall", new Vector3(0f, 0f, -8f), G["Wall_South"]);
        yield return BuildBox("KickWall", G["Wall_East"], new Vector3(15.95f, 0f, -4f), new Vector3(16.05f, 0.4f, 4f), mFrame);
        yield return BuildBox("KickWall_South", G["Wall_West"], new Vector3(3.95f, 0f, -4f), new Vector3(4.05f, 0.4f, -1.25f), mFrame);
        yield return Duplicate(cur.gameObject, "KickWall_North", new Vector3(0f, 0f, 5.25f));
        yield return CheckPhase(new Vector3(10f, 0.5f, 0f), 9f);

        // ===== Fase 3 — Tiang =====
        yield return CamTo(new Vector3(10f, 1.5f, 0f), 30f, 30f, 10f, 1.5f);
        yield return BuildBox("Post_Corner_NW", G["WingC_Shell"], new Vector3(3.96f, 0f, 3.96f), new Vector3(4.04f, 3f, 4.04f), mFrame);
        GameObject post = cur.gameObject;

        for (int i = 1; i <= 5; i++)                    // utara, X = 6..14
        {
            yield return Duplicate(post, $"Post_{i:00}", new Vector3(2f, 0f, 0f), i == 1 ? G["Wall_North"] : null);
            post = cur.gameObject;
        }
        yield return Duplicate(post, "Post_Corner_NE", new Vector3(2f, 0f, 0f), G["WingC_Shell"]);
        post = cur.gameObject;

        for (int i = 1; i <= 3; i++)                    // timur, Z = 2, 0, -2
        {
            yield return Duplicate(post, $"Post_{i:00}", new Vector3(0f, 0f, -2f), i == 1 ? G["Wall_East"] : null);
            post = cur.gameObject;
        }
        yield return Duplicate(post, "Post_Corner_SE", new Vector3(0f, 0f, -2f), G["WingC_Shell"]);
        post = cur.gameObject;

        for (int i = 5; i >= 1; i--)                    // selatan, X = 14..6
        {
            yield return Duplicate(post, $"Post_{i:00}", new Vector3(-2f, 0f, 0f), i == 5 ? G["Wall_South"] : null);
            post = cur.gameObject;
        }
        yield return Duplicate(post, "Post_Corner_SW", new Vector3(-2f, 0f, 0f), G["WingC_Shell"]);
        post = cur.gameObject;

        yield return Duplicate(post, "DoorJamb_South", new Vector3(0f, 0f, 2.75f), G["Wall_West"]);   // Z = -1.25
        yield return Duplicate(cur.gameObject, "DoorJamb_North", new Vector3(0f, 0f, 2.5f));          // Z = 1.25
        yield return CheckPhase(new Vector3(10f, 1.5f, 0f), 10f);

        // ===== Fase 4 — Balok =====
        yield return CamTo(new Vector3(10f, 2f, 0f), 30f, 30f, 10f, 1.5f);
        yield return BuildBox("EaveBeam", G["Wall_North"], new Vector3(4f, 2.95f, 3.95f), new Vector3(16f, 3.05f, 4.05f), mFrame);
        yield return Duplicate(cur.gameObject, "EaveBeam", new Vector3(0f, 0f, -8f), G["Wall_South"]);
        yield return BuildBox("EaveBeam", G["Wall_East"], new Vector3(15.95f, 2.95f, -4f), new Vector3(16.05f, 3.05f, 4f), mFrame);
        yield return Duplicate(cur.gameObject, "EaveBeam", new Vector3(-12f, 0f, 0f), G["Wall_West"]);

        yield return BuildBox("TransomBar", G["Wall_North"], new Vector3(4f, 1.58f, 3.98f), new Vector3(16f, 1.62f, 4.02f), mFrame);
        yield return Duplicate(cur.gameObject, "TransomBar", new Vector3(0f, 0f, -8f), G["Wall_South"]);
        yield return BuildBox("TransomBar", G["Wall_East"], new Vector3(15.98f, 1.58f, -4f), new Vector3(16.02f, 1.62f, 4f), mFrame);
        yield return BuildBox("TransomBar_South", G["Wall_West"], new Vector3(3.98f, 1.58f, -4f), new Vector3(4.02f, 1.62f, -1.25f), mFrame);
        yield return Duplicate(cur.gameObject, "TransomBar_North", new Vector3(0f, 0f, 5.25f));

        yield return BuildBox("DoorHeader", G["Wall_West"], new Vector3(3.95f, 2.75f, -1.25f), new Vector3(4.05f, 2.85f, 1.25f), mFrame);
        yield return CheckPhase(new Vector3(10f, 1.5f, 0f), 10f);

        // ===== Fase 5 — Kaca dinding =====
        yield return CamTo(new Vector3(10f, 1.5f, 0f), 30f, 30f, 10f, 1.5f);
        yield return BuildBox("GlassPane", G["Wall_North"], new Vector3(4f, 0.4f, 3.99f), new Vector3(16f, 3f, 4.01f), mGlass);
        yield return Duplicate(cur.gameObject, "GlassPane", new Vector3(0f, 0f, -8f), G["Wall_South"]);
        yield return BuildBox("GlassPane", G["Wall_East"], new Vector3(15.99f, 0.4f, -4f), new Vector3(16.01f, 3f, 4f), mGlass);
        yield return BuildBox("GlassPane_South", G["Wall_West"], new Vector3(3.99f, 0.4f, -4f), new Vector3(4.01f, 3f, -1.25f), mGlass);
        yield return Duplicate(cur.gameObject, "GlassPane_North", new Vector3(0f, 0f, 5.25f));
        yield return BuildBox("GlassPane_AboveDoor", G["Wall_West"], new Vector3(3.99f, 2.8f, -1.25f), new Vector3(4.01f, 3f, 1.25f), mGlass);
        yield return CheckPhase(new Vector3(10f, 1.5f, 0f), 10f);

        // ===== Fase 6 — Rangka atap =====
        yield return CreateGroup("Roof", "WingC_Shell");
        yield return CamTo(new Vector3(10f, 3.5f, 0f), 30f, 32f, 9f, 1.5f);
        yield return BuildBox("RidgeBeam", G["Roof"], new Vector3(4f, 4.94f, -0.06f), new Vector3(16f, 5.06f, 0.06f), mFrame);

        float h = RafterLen / 2f;
        // Pemula belum tahu panjang & sudut kasau: bikin 4 m dulu (jarak datar),
        // putar kira-kira sampai pas, cari angka pastinya, lalu panjangkan ujungnya.
        yield return BuildBox("Rafter_N_01", G["Roof"], new Vector3(3.965f, 3.965f, 0f), new Vector3(4.035f, 4.035f, 4f), mFrame);
        Vector3 ridgePt = new Vector3(4f, 5f, 0f), eavePt = new Vector3(4f, 3f, 4f);
        yield return EyeballRafter(cur, ridgePt, eavePt);
        yield return DragLocal(cur, N, h, eavePt);    // ujung bawah sampai eave
        yield return DragLocal(cur, S, h, ridgePt);   // ujung atas sampai ridge
        yield return CamTo(new Vector3(10f, 3.5f, 0f), 30f, 32f, 9f, 1.5f);
        GameObject raf = cur.gameObject;
        for (int i = 2; i <= 7; i++)                    // X = 6..16
        {
            yield return Duplicate(raf, $"Rafter_N_{i:00}", new Vector3(2f, 0f, 0f));
            raf = cur.gameObject;
        }
        yield return Duplicate(raf, "Rafter_S_07", new Vector3(0f, 0f, -4f), null, new Vector3(-RoofAngle, 0f, 0f));
        raf = cur.gameObject;
        for (int i = 6; i >= 1; i--)                    // X = 14..4
        {
            yield return Duplicate(raf, $"Rafter_S_{i:00}", new Vector3(-2f, 0f, 0f));
            raf = cur.gameObject;
        }

        yield return BuildBox("Purlin_North", G["Roof"], new Vector3(4f, 3.98f, 1.98f), new Vector3(16f, 4.02f, 2.02f), mFrame);
        yield return Duplicate(cur.gameObject, "Purlin_South", new Vector3(0f, 0f, -4f));
        yield return CheckPhase(new Vector3(10f, 3f, 0f), 10f);

        // ===== Fase 7 — Kaca atap =====
        yield return CamTo(new Vector3(10f, 3.5f, 0f), 30f, 32f, 9f, 1.5f);
        yield return BuildBox("Glass_Slope_North", G["Roof"], new Vector3(4f, 3.99f, 2f - h), new Vector3(16f, 4.01f, 2f + h), mGlass);
        yield return RotateTo(cur.transform, new Vector3(RoofAngle, 0f, 0f));
        yield return Duplicate(cur.gameObject, "Glass_Slope_South", new Vector3(0f, 0f, -4f), null, new Vector3(-RoofAngle, 0f, 0f));

        yield return BuildBox("Glass_Gable_West", G["Roof"], new Vector3(3.99f, 3f, -4f), new Vector3(4.01f, 5f, 4f), mGlass);
        yield return CollapseTop(cur);
        yield return Duplicate(cur.gameObject, "Glass_Gable_East", new Vector3(12f, 0f, 0f));
        yield return CheckPhase(new Vector3(10f, 3f, 0f), 10f);

        // ===== Fase 8 — Papan nama =====
        yield return BuildBox("Sign_WingC", G["WingC_Shell"], new Vector3(3.88f, 3.05f, -1.2f), new Vector3(3.92f, 3.45f, 1.2f), mSoil);
        yield return CheckPhase(new Vector3(6f, 2.5f, 0f), 6f);

        // ===== Selesai =====
        Selection.activeGameObject = null;
        yield return Orbit(new Vector3(10f, 2f, 0f), 12f, 10f);
        Selection.activeGameObject = G["WingC_Shell"].gameObject;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }
}
