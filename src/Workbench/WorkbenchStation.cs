using System;
using System.IO;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace RackMedic.Workbench
{
    /// <summary>
    /// Physical workbench prop placed in the game world.
    /// When the player approaches (within 2.5 units) and presses the Workbench key (default F7),
    /// the WorkbenchUI opens in server-picker mode rather than trying to find a nearby server.
    /// Position is persisted in UserData/RackMedic/workbench_pos.txt.
    /// </summary>
    public class WorkbenchStation : MonoBehaviour
    {
        public WorkbenchStation(IntPtr ptr) : base(ptr) { }

        // ── Singleton ─────────────────────────────────────────────────────────
        public static WorkbenchStation Instance { get; private set; }

        // ── Proximity state (read by WorkbenchUI) ─────────────────────────────
        public bool IsPlayerNearby { get; private set; }

        // ── Constants ─────────────────────────────────────────────────────────
        private const float INTERACT_RANGE  = 2.5f;
        private const string POS_FILE       = "UserData/RackMedic/workbench_pos.txt";

        // ── Hint rendering ────────────────────────────────────────────────────
        private GUIStyle  _hintStyle;
        private Texture2D _hintBgTex;
        private bool      _hintStyleReady;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            LoadOrPlaceInitial();
            BuildMesh();
        }

        private void Update()
        {
            try
            {
                var pm = PlayerManager.instance;
                if (pm?.playerGO != null)
                {
                    float dist = Vector3.Distance(transform.position,
                                                  pm.playerGO.transform.position);
                    IsPlayerNearby = dist <= INTERACT_RANGE;
                }
                else
                {
                    IsPlayerNearby = false;
                }
            }
            catch
            {
                IsPlayerNearby = false;
            }
        }

        private void OnGUI()
        {
            if (!IsPlayerNearby) return;

            EnsureHintStyle();

            float cx     = Screen.width  / 2f;
            float cy     = Screen.height * 0.72f;
            var   hintR  = new Rect(cx - 180f, cy, 360f, 30f);

            GUI.color = new Color(1f, 1f, 1f, 0.88f);
            GUI.DrawTexture(hintR, _hintBgTex);
            GUI.color = Color.white;

            GUI.Label(hintR,
                $"[{RackMedicMod.WorkbenchKey}]  Open Workbench",
                _hintStyle);
        }

        // ── Hint style ────────────────────────────────────────────────────────

        private void EnsureHintStyle()
        {
            if (_hintStyleReady) return;

            _hintBgTex = new Texture2D(1, 1);
            var bgPx = new Color[] { new Color(0.05f, 0.05f, 0.05f, 0.78f) };
            _hintBgTex.SetPixels(bgPx);
            _hintBgTex.Apply();
            UnityEngine.Object.DontDestroyOnLoad(_hintBgTex);

            var pad = new RectOffset();
            pad.left = 0; pad.right = 0; pad.top = 0; pad.bottom = 0;

            _hintStyle = new GUIStyle()
            {
                fontSize  = 14,
                alignment = TextAnchor.MiddleCenter,
                padding   = pad,
                normal    = { textColor = Color.white }
            };

            _hintStyleReady = true;
        }

        // ── World placement ───────────────────────────────────────────────────

        private void LoadOrPlaceInitial()
        {
            if (File.Exists(POS_FILE))
            {
                try
                {
                    string[] parts = File.ReadAllText(POS_FILE).Trim().Split(',');
                    if (parts.Length == 3 &&
                        float.TryParse(parts[0], out float px) &&
                        float.TryParse(parts[1], out float py) &&
                        float.TryParse(parts[2], out float pz))
                    {
                        transform.position = new Vector3(px, py, pz);
                        return;
                    }
                }
                catch { /* fall through to PlaceNearPlayer */ }
            }

            PlaceNearPlayer();
        }

        private void PlaceNearPlayer()
        {
            Vector3 pos = Vector3.zero;
            try
            {
                var pm = PlayerManager.instance;
                if (pm?.playerGO != null)
                {
                    var t = pm.playerGO.transform;
                    // Place 3 units to the right of the player, at the same Y
                    pos = t.position + t.right * 3f;
                    pos.y = t.position.y;
                }
            }
            catch { }

            transform.position = pos;
            SavePosition();
        }

        private void SavePosition()
        {
            try
            {
                Directory.CreateDirectory("UserData/RackMedic");
                var p = transform.position;
                File.WriteAllText(POS_FILE,
                    $"{p.x.ToString("F3")},{p.y.ToString("F3")},{p.z.ToString("F3")}");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[WorkbenchStation] Could not save position: {ex.Message}");
            }
        }

        // ── Visual mesh ───────────────────────────────────────────────────────

        private void BuildMesh()
        {
            try
            {
                // Table-top (flat cube)
                var top = GameObject.CreatePrimitive(PrimitiveType.Cube);
                top.transform.SetParent(transform);
                top.transform.localPosition = new Vector3(0f, 0.48f, 0f);
                top.transform.localScale    = new Vector3(1.4f, 0.08f, 0.7f);
                ApplyColor(top, new Color(0.35f, 0.22f, 0.10f));

                // Four legs
                float lx = 0.60f, lz = 0.28f;
                Vector3[] legOffsets = {
                    new Vector3(-lx, 0.22f, -lz), new Vector3( lx, 0.22f, -lz),
                    new Vector3(-lx, 0.22f,  lz), new Vector3( lx, 0.22f,  lz)
                };
                foreach (var offset in legOffsets)
                {
                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    leg.transform.SetParent(transform);
                    leg.transform.localPosition = offset;
                    leg.transform.localScale    = new Vector3(0.07f, 0.24f, 0.07f);
                    ApplyColor(leg, new Color(0.28f, 0.18f, 0.08f));
                }

                // Lower shelf
                var shelf = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shelf.transform.SetParent(transform);
                shelf.transform.localPosition = new Vector3(0f, 0.15f, 0f);
                shelf.transform.localScale    = new Vector3(1.2f, 0.04f, 0.55f);
                ApplyColor(shelf, new Color(0.30f, 0.20f, 0.09f));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[WorkbenchStation] BuildMesh error: {ex.Message}");
            }
        }

        private static void ApplyColor(GameObject go, Color color)
        {
            try
            {
                var rend = go.GetComponent<MeshRenderer>();
                if (rend == null) return;
                var shader = Shader.Find("Standard") ?? Shader.Find("Sprites/Default");
                if (shader == null) return;
                var mat = new Material(shader);
                mat.color = color;
                rend.material = mat;
            }
            catch { /* Non-critical — object will appear white */ }
        }
    }
}
