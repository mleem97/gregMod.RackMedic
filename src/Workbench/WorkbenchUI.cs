using System;
using System.Collections.Generic;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using RackMedic.Core;

namespace RackMedic.Workbench
{
    /// <summary>
    /// Workbench panel (default key: F7).
    /// Opened when the player presses WorkbenchKey while looking at any server rack.
    /// Shows per-slot health, lets the player remove and install components from
    /// their inventory.
    /// </summary>
    public class WorkbenchUI : MonoBehaviour
    {
        // ── Mode ──────────────────────────────────────────────────────────────
        private enum WorkbenchMode { Picker, Detail }
        private WorkbenchMode _mode = WorkbenchMode.Detail;

        // ── State ─────────────────────────────────────────────────────────────
        private bool _open;
        private string _serverId;               // server currently on the bench
        private int    _selectedSlotIndex = -1; // flat index into slot list
        private Vector2 _slotScroll;
        private Vector2 _detailScroll;
        private bool _reenableNextFrame;

        // ── Layout constants ──────────────────────────────────────────────────
        private const float WIN_W = 780f;
        private const float WIN_H = 520f;
        private Rect _winRect;

        // Colour palette
        private static Color ColOk      = new Color(0.22f, 0.78f, 0.22f);
        private static Color ColDegraded= new Color(0.95f, 0.75f, 0.10f);
        private static Color ColFailed  = new Color(0.90f, 0.18f, 0.18f);
        private static Color ColEmpty   = new Color(0.45f, 0.45f, 0.45f);

        // GUIStyles (lazy-initialised)
        private GUIStyle _winStyle, _titleStyle, _slotBtnStyle, _slotSelStyle;
        private GUIStyle _labelStyle, _dimStyle, _installBtnStyle, _removeBtnStyle;
        private GUIStyle _headerStyle;
        private bool _stylesReady;

        // Background texture
        private Texture2D _bgTex;
        private Texture2D _slotTex, _slotSelTex, _healthBarBg, _healthBarFg;

        // ── Flat slot list built per-open ─────────────────────────────────────
        // Each entry: (slotName, slot)
        private List<(string name, ComponentSlot slot)> _slots = new();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            float x = (Screen.width  - WIN_W) / 2f;
            float y = (Screen.height - WIN_H) / 2f;
            _winRect = new Rect(x, y, WIN_W, WIN_H);
        }

        private void Update()
        {
            if (_reenableNextFrame)
            {
                _reenableNextFrame = false;
                EnableInput();
            }
        }

        private void OnGUI()
        {
            // Key detection via Event.current works regardless of Input System setting
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                if (!_open && e.keyCode == RackMedicMod.WorkbenchKey)
                { TryOpenForLookedAtServer(); e.Use(); return; }
                if (_open && e.keyCode == KeyCode.Escape)
                { Close(); e.Use(); return; }
            }
            if (!_open) return;
            EnsureStyles();

            // Picker mode: show server selection list instead of the main workbench panel
            if (_mode == WorkbenchMode.Picker)
            {
                // Dark overlay
                GUI.color = new Color(0, 0, 0, 0.55f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bgTex);
                GUI.color = Color.white;

                GUI.Box(_winRect, string.Empty, _winStyle);
                GUI.BeginGroup(_winRect);
                DrawServerPicker();
                GUI.EndGroup();
                return;
            }

            var profile = ComponentStore.Instance?.Get(_serverId);
            if (profile == null) { Close(); return; }

            // Dark overlay
            GUI.color = new Color(0, 0, 0, 0.55f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bgTex);
            GUI.color = Color.white;

            GUI.Box(_winRect, string.Empty, _winStyle);
            GUI.BeginGroup(_winRect);
            DrawWindow();
            GUI.EndGroup();
        }

        // ── Window drawing ────────────────────────────────────────────────────

        private void DrawWindow()
        {
            var profile = ComponentStore.Instance?.Get(_serverId);
            if (profile == null) return;

            float pad = 10f;
            float y   = pad;

            // ── Title row ────────────────────────────────────────────────────
            GUI.Label(new Rect(pad, y, WIN_W - 220f, 26f),
                $"Workbench  —  {_serverId}", _titleStyle);

            // Back to server list (only when opened via physical workbench)
            if (WorkbenchStation.Instance != null && WorkbenchStation.Instance.IsPlayerNearby)
            {
                if (GUI.Button(new Rect(WIN_W - pad - 180f, y - 2f, 92f, 26f), "← Servers"))
                { OpenServerPicker(); return; }
            }

            string uptime = $"{profile.TotalPoweredOnHours:F1}h powered on";
            GUI.Label(new Rect(WIN_W - 200f, y, 190f, 22f), uptime, _dimStyle);

            if (GUI.Button(new Rect(WIN_W - pad - 80f, y - 2f, 80f, 26f), "Close"))
            { Close(); return; }

            y += 32f;

            // ── Status summary ────────────────────────────────────────────────
            string summary = GetSummary(profile);
            GUI.Label(new Rect(pad, y, WIN_W - 2 * pad, 22f), summary, _dimStyle);
            y += 26f;

            // ── Divider ───────────────────────────────────────────────────────
            GUI.color = new Color(0.35f, 0.35f, 0.35f);
            GUI.DrawTexture(new Rect(pad, y, WIN_W - 2 * pad, 1f), _bgTex);
            GUI.color = Color.white;
            y += 6f;

            // ── Two-column layout ─────────────────────────────────────────────
            float leftW  = 310f;
            float rightW = WIN_W - leftW - pad * 3;
            float colH   = WIN_H - y - 50f;

            // Left: slot list
            DrawSlotList(new Rect(pad, y, leftW, colH), profile);

            // Right: selected slot detail
            DrawSlotDetail(new Rect(pad * 2 + leftW, y, rightW, colH), profile);

            // ── Bottom bar ────────────────────────────────────────────────────
            float botY = WIN_H - 40f;
            GUI.color = new Color(0.35f, 0.35f, 0.35f);
            GUI.DrawTexture(new Rect(pad, botY - 4f, WIN_W - 2 * pad, 1f), _bgTex);
            GUI.color = Color.white;

            string power = $"Power draw: {profile.TotalPowerDrawWatts:F0} W";
            GUI.Label(new Rect(pad, botY, 250f, 26f), power, _dimStyle);

        }

        private void DrawSlotList(Rect area, ServerProfile profile)
        {
            BuildSlotList(profile);

            GUI.Label(new Rect(area.x, area.y, area.width, 20f), "COMPONENTS", _headerStyle);
            float listY = area.y + 22f;
            float listH = area.height - 22f;

            var viewRect = new Rect(area.x, listY, area.width, listH);
            var contentRect = new Rect(0, 0, area.width - 16f, _slots.Count * 52f);
            _slotScroll = GUI.BeginScrollView(viewRect, _slotScroll, contentRect);

            for (int i = 0; i < _slots.Count; i++)
            {
                var (name, slot) = _slots[i];
                bool selected = _selectedSlotIndex == i;
                DrawSlotRow(i, name, slot, selected, area.width - 18f);
            }

            GUI.EndScrollView();
        }

        private void DrawSlotRow(int index, string name, ComponentSlot slot, bool selected, float width)
        {
            float rowY = index * 52f;
            var rowRect = new Rect(0, rowY, width, 48f);

            // Background button
            GUIStyle style = selected ? _slotSelStyle : _slotBtnStyle;
            if (GUI.Button(rowRect, string.Empty, style))
                _selectedSlotIndex = index;

            // Status colour dot (4×4 box)
            Color dotCol = StatusColor(slot.Status);
            GUI.color = dotCol;
            GUI.DrawTexture(new Rect(6, rowY + 6, 8f, 8f), _bgTex);
            GUI.color = Color.white;

            // Slot name
            GUI.Label(new Rect(20f, rowY + 4f, width - 80f, 20f), name, _labelStyle);

            // Component name or EMPTY
            string compName = slot.Status == ComponentStatus.Empty
                ? "<color=#666>empty</color>"
                : $"<color=#ccc>{slot.Def?.DisplayName ?? slot.DefId}</color>";
            GUI.Label(new Rect(20f, rowY + 22f, width - 80f, 18f), compName, _dimStyle);

            // Health bar (right side)
            if (slot.Status != ComponentStatus.Empty)
            {
                float barW = 55f;
                float barH = 8f;
                float barX = width - barW - 6f;
                float barY = rowY + 10f;

                // Background
                GUI.color = new Color(0.2f, 0.2f, 0.2f);
                GUI.DrawTexture(new Rect(barX, barY, barW, barH), _healthBarBg);

                // Fill
                float fill = Mathf.Clamp01(slot.Health / 100f);
                GUI.color = dotCol;
                if (fill > 0f)
                    GUI.DrawTexture(new Rect(barX, barY, barW * fill, barH), _healthBarFg);

                GUI.color = Color.white;

                // Percentage text
                string pct = slot.Status == ComponentStatus.Failed ? "FAILED"
                           : $"{slot.Health:F0}%";
                GUI.Label(new Rect(barX, barY + barH + 1f, barW, 14f), pct, _dimStyle);
            }
        }

        private void DrawSlotDetail(Rect area, ServerProfile profile)
        {
            GUI.Label(new Rect(area.x, area.y, area.width, 20f), "SLOT DETAIL", _headerStyle);
            float detY = area.y + 24f;
            float detH = area.height - 24f;

            if (_selectedSlotIndex < 0 || _selectedSlotIndex >= _slots.Count)
            {
                GUI.Label(new Rect(area.x, detY, area.width, 20f), "Select a slot on the left", _dimStyle);
                return;
            }

            var (slotName, slot) = _slots[_selectedSlotIndex];

            // Slot heading
            GUI.Label(new Rect(area.x, detY, area.width, 22f), slotName, _labelStyle);
            detY += 24f;

            // Installed component info
            if (slot.Status == ComponentStatus.Empty)
            {
                GUI.Label(new Rect(area.x, detY, area.width, 20f), "No component installed", _dimStyle);
                detY += 22f;
            }
            else
            {
                var def = slot.Def;
                string installed = def != null ? def.DisplayName : slot.DefId;
                GUI.Label(new Rect(area.x, detY, area.width, 20f), installed, _labelStyle);
                detY += 20f;

                Color sc = StatusColor(slot.Status);
                string statusText = slot.Status.ToString().ToUpper();
                GUI.color = sc;
                GUI.Label(new Rect(area.x, detY, area.width, 18f), statusText, _dimStyle);
                GUI.color = Color.white;
                detY += 20f;

                GUI.Label(new Rect(area.x, detY, area.width, 18f),
                    $"Health: {slot.Health:F1}%  |  Hours: {slot.PoweredOnHours:F1}h", _dimStyle);
                detY += 20f;

                if (def != null)
                {
                    GUI.Label(new Rect(area.x, detY, area.width, 18f),
                        $"{def.Description}", _dimStyle);
                    detY += 20f;
                }

                detY += 6f;

                // Remove button
                if (GUI.Button(new Rect(area.x, detY, 120f, 26f), "Remove", _removeBtnStyle))
                    RemoveComponent(profile, _selectedSlotIndex);

                detY += 34f;
            }

            // Divider
            GUI.color = new Color(0.3f, 0.3f, 0.3f);
            GUI.DrawTexture(new Rect(area.x, detY, area.width, 1f), _bgTex);
            GUI.color = Color.white;
            detY += 8f;

            // Compatible inventory items
            GUI.Label(new Rect(area.x, detY, area.width, 18f), "Available in inventory:", _labelStyle);
            detY += 22f;

            var compatible = GetCompatibleInventory(slotName);
            var scrollContentH = compatible.Count * 38f;
            _detailScroll = GUI.BeginScrollView(
                new Rect(area.x, detY, area.width, detH - (detY - area.y)),
                _detailScroll,
                new Rect(0, 0, area.width - 16f, scrollContentH));

            for (int i = 0; i < compatible.Count; i++)
            {
                var (compId, count) = compatible[i];
                var def = ShopCatalog.FindComponent(compId);
                if (def == null) continue;

                float ry = i * 38f;
                GUI.Label(new Rect(4f, ry + 4f, area.width - 100f, 20f),
                    $"{def.DisplayName} (×{count})", _labelStyle);
                GUI.Label(new Rect(4f, ry + 22f, area.width - 100f, 16f),
                    def.Description, _dimStyle);

                if (GUI.Button(new Rect(area.width - 100f, ry + 6f, 80f, 26f), "Install", _installBtnStyle))
                    InstallComponent(profile, _selectedSlotIndex, compId);
            }

            GUI.EndScrollView();
        }

        // ── Install / Remove logic ─────────────────────────────────────────────

        private void RemoveComponent(ServerProfile profile, int slotIndex)
        {
            var (name, slot) = _slots[slotIndex];
            if (slot == null || string.IsNullOrEmpty(slot.DefId)) return;

            string defId = slot.DefId;
            slot.DefId = null;
            slot.Health = 100f;
            slot.PoweredOnHours = 0f;

            ComponentStore.Instance.AddToInventory(defId, 1);
            ComponentStore.Instance.Save();
            FailureEngine.Instance?.ReevaluateServer(profile.ServerId);
            RebuildSlots(profile);
            _selectedSlotIndex = slotIndex;
            MelonLogger.Msg($"[RackMedic] Removed {defId} from {name} on {profile.ServerId}");
        }

        private void InstallComponent(ServerProfile profile, int slotIndex, string defId)
        {
            var (name, slot) = _slots[slotIndex];
            if (slot == null) return;

            // Remove anything currently in slot first
            if (!string.IsNullOrEmpty(slot.DefId))
                RemoveComponent(profile, slotIndex);

            if (!ComponentStore.Instance.ConsumeFromInventory(defId))
            {
                MelonLogger.Warning($"[RackMedic] Not enough {defId} in inventory");
                return;
            }

            slot.DefId = defId;
            slot.Health = 100f;
            slot.PoweredOnHours = 0f;
            ComponentStore.Instance.Save();
            FailureEngine.Instance?.ReevaluateServer(profile.ServerId);
            RebuildSlots(profile);
            _selectedSlotIndex = slotIndex;
            MelonLogger.Msg($"[RackMedic] Installed {defId} in {name} on {profile.ServerId}");
        }

        // ── Slot list building ────────────────────────────────────────────────

        private void BuildSlotList(ServerProfile profile) => RebuildSlots(profile);

        private void RebuildSlots(ServerProfile profile)
        {
            _slots.Clear();
            _slots.Add(("CPU", profile.Cpu));
            if (profile.Ram != null)
                for (int i = 0; i < profile.Ram.Length; i++)
                    _slots.Add(($"RAM Slot {i + 1}", profile.Ram[i]));
            _slots.Add(("PSU", profile.Psu));
            _slots.Add(("NIC", profile.Nic));
            if (profile.Storage != null)
                for (int i = 0; i < profile.Storage.Length; i++)
                    _slots.Add(($"Storage {i + 1}", profile.Storage[i]));
            _slots.Add(("Cooling", profile.Cooling));
        }

        private List<(string id, int count)> GetCompatibleInventory(string slotName)
        {
            var result = new List<(string id, int count)>();
            var inv = ComponentStore.Instance?.Inventory;
            if (inv == null) return result;

            ComponentType? targetType = SlotNameToType(slotName);
            if (targetType == null) return result;

            foreach (var kv in inv)
            {
                if (kv.Value <= 0) continue;
                var def = ShopCatalog.FindComponent(kv.Key);
                if (def == null) continue;
                if (def.Type == targetType.Value)
                    result.Add((kv.Key, kv.Value));
            }

            // Sort by tier descending
            result.Sort((a, b) =>
            {
                var da = ShopCatalog.FindComponent(a.id);
                var db = ShopCatalog.FindComponent(b.id);
                return (db?.Tier ?? 0).CompareTo(da?.Tier ?? 0);
            });

            return result;
        }

        private static ComponentType? SlotNameToType(string name)
        {
            if (name == "CPU")           return ComponentType.CPU;
            if (name.StartsWith("RAM"))  return ComponentType.RAM;
            if (name == "PSU")           return ComponentType.PSU;
            if (name == "NIC")           return ComponentType.NIC;
            if (name.StartsWith("Storage")) return ComponentType.StorageDrive;
            if (name == "Cooling")       return ComponentType.Cooling;
            return null;
        }

        private static string GetSummary(ServerProfile profile)
        {
            if (profile.HasAnyCriticalFailure)
                return $"<color=#e83>CRITICAL FAILURE</color>  |  " +
                       string.Join(", ", profile.GetFailedNames());
            if (!profile.AllComponentsHealthy)
                return $"<color=#fb3>Degraded</color>  |  " +
                       string.Join(", ", profile.GetFailedNames());
            return "<color=#3a3>All components healthy</color>";
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        private void TryOpenForLookedAtServer()
        {
            // If standing at the physical workbench, show server picker instead
            if (WorkbenchStation.Instance != null && WorkbenchStation.Instance.IsPlayerNearby)
            {
                OpenServerPicker();
                return;
            }

            // Find nearest managed server (simple distance check)
            var store = ComponentStore.Instance;
            if (store == null || store.Profiles.Count == 0) return;

            // Look for a server within 5 units of the camera
            var cam = Camera.main;
            if (cam == null) return;

            Server closest = null;
            float closestDist = 5f;
            foreach (var srv in UnityEngine.Object.FindObjectsOfType<Server>())
            {
                string sid = GameAccess.GetServerId(srv);
                if (string.IsNullOrEmpty(sid)) continue;
                if (!store.IsManaged(sid)) continue;

                float d = Vector3.Distance(cam.transform.position, srv.transform.position);
                if (d < closestDist) { closestDist = d; closest = srv; }
            }

            if (closest == null) return; // nothing in range — silently ignore

            string foundId = GameAccess.GetServerId(closest);
            if (string.IsNullOrEmpty(foundId)) return;
            Open(foundId, ComponentStore.Instance.Get(foundId));
        }

        private void Open(string serverId, ServerProfile profile)
        {
            if (profile == null) return;
            _serverId = serverId;
            _mode = WorkbenchMode.Detail;
            _selectedSlotIndex = -1;
            _slotScroll = Vector2.zero;
            _detailScroll = Vector2.zero;
            RebuildSlots(profile);
            _open = true;
            DisableInput();
        }

        private void Close()
        {
            _open = false;
            _reenableNextFrame = true;
        }

        public void OpenForServer(string serverId)
        {
            var profile = ComponentStore.Instance?.Get(serverId);
            if (profile != null) Open(serverId, profile);
        }

        public void OpenServerPicker()
        {
            _mode = WorkbenchMode.Picker;
            _selectedSlotIndex = -1;
            _slotScroll = Vector2.zero;
            _open = true;
            DisableInput();
        }

        private void DrawServerPicker()
        {
            var store = ComponentStore.Instance;
            float pad = 10f;
            float y   = pad;

            GUI.Label(new Rect(pad, y, WIN_W - 100f, 26f),
                "Workbench  —  Select Server", _titleStyle);

            if (GUI.Button(new Rect(WIN_W - pad - 80f, y - 2f, 80f, 26f), "Close"))
            { Close(); return; }

            y += 36f;

            if (store == null || store.Profiles.Count == 0)
            {
                GUI.Label(new Rect(pad, y, WIN_W - 2 * pad, 40f),
                    "No managed servers found.\nMove close to a server rack and press F7 to register it first.",
                    _dimStyle);
                return;
            }

            GUI.Label(new Rect(pad, y, WIN_W - 2 * pad, 20f),
                "Pick a server to work on:", _dimStyle);
            y += 26f;

            // Scrollable server list
            var ids = new System.Collections.Generic.List<string>(store.Profiles.Keys);
            float listH = WIN_H - y - 10f;
            float rowH  = 52f;
            var scrollContent = new Rect(0, 0, WIN_W - 2 * pad - 16f, ids.Count * rowH);
            _slotScroll = GUI.BeginScrollView(
                new Rect(pad, y, WIN_W - 2 * pad, listH),
                _slotScroll, scrollContent);

            for (int i = 0; i < ids.Count; i++)
            {
                string sid     = ids[i];
                var    profile = store.Get(sid);
                if (profile == null) continue;

                float ry = i * rowH;
                var rowR = new Rect(0, ry, scrollContent.width, rowH - 4f);
                GUI.DrawTexture(rowR, _slotTex);

                // Health dot
                Color dot = profile.HasAnyCriticalFailure ? ColFailed
                          : profile.AllComponentsHealthy   ? ColOk
                          :                                   ColDegraded;
                GUI.color = dot;
                GUI.DrawTexture(new Rect(8f, ry + 7f, 8f, 8f), _bgTex);
                GUI.color = Color.white;

                GUI.Label(new Rect(22f, ry + 4f, scrollContent.width - 120f, 20f), sid, _labelStyle);

                string status = profile.HasAnyCriticalFailure ? "CRITICAL FAILURE"
                              : profile.AllComponentsHealthy   ? "Healthy"
                              :                                   "Degraded";
                GUI.color = dot;
                GUI.Label(new Rect(22f, ry + 24f, scrollContent.width - 120f, 18f), status, _dimStyle);
                GUI.color = Color.white;

                if (GUI.Button(new Rect(scrollContent.width - 90f, ry + 10f, 82f, 28f),
                               "Open", _installBtnStyle))
                {
                    GUI.EndScrollView();
                    Open(sid, profile);
                    return;
                }
            }

            GUI.EndScrollView();
        }

        // ── Input disable/enable ────────────────────────────────────────────

        private static void DisableInput()
        {
            try
            {
                InputSystem.DisableDevice(Keyboard.current);
                InputSystem.DisableDevice(Mouse.current);
            }
            catch { /* InputSystem may not be ready */ }
        }

        private static void EnableInput()
        {
            try
            {
                if (Keyboard.current != null) InputSystem.EnableDevice(Keyboard.current);
                if (Mouse.current    != null) InputSystem.EnableDevice(Mouse.current);
            }
            catch { }
        }

        // ── Style initialisation ──────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _bgTex = MakeTex(2, 2, Color.white);
            UnityEngine.Object.DontDestroyOnLoad(_bgTex);

            _slotTex    = MakeTex(2, 2, new Color(0.15f, 0.15f, 0.15f, 0.95f));
            _slotSelTex = MakeTex(2, 2, new Color(0.22f, 0.35f, 0.50f, 0.95f));
            _healthBarBg = MakeTex(2, 2, new Color(0.2f, 0.2f, 0.2f));
            _healthBarFg = MakeTex(2, 2, Color.white);
            UnityEngine.Object.DontDestroyOnLoad(_slotTex);
            UnityEngine.Object.DontDestroyOnLoad(_slotSelTex);
            UnityEngine.Object.DontDestroyOnLoad(_healthBarBg);
            UnityEngine.Object.DontDestroyOnLoad(_healthBarFg);

            var winBg = MakeTex(2, 2, new Color(0.08f, 0.08f, 0.10f, 0.97f));
            UnityEngine.Object.DontDestroyOnLoad(winBg);

            _winStyle = new GUIStyle()
            {
                normal = { background = winBg },
                padding = new RectOffset()
            };

            _titleStyle = new GUIStyle()
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                richText = true
            };

            _headerStyle = new GUIStyle()
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) }
            };

            _labelStyle = new GUIStyle()
            {
                fontSize = 12,
                normal = { textColor = Color.white },
                richText = true
            };

            _dimStyle = new GUIStyle()
            {
                fontSize = 11,
                normal = { textColor = new Color(0.65f, 0.65f, 0.65f) },
                richText = true
            };

            _slotBtnStyle = new GUIStyle()
            {
                normal  = { background = _slotTex, textColor = Color.clear },
                hover   = { background = MakeTex(2, 2, new Color(0.20f, 0.20f, 0.22f, 0.95f)), textColor = Color.clear },
                active  = { background = _slotSelTex, textColor = Color.clear },
                padding = new RectOffset()
            };
            UnityEngine.Object.DontDestroyOnLoad(_slotBtnStyle.hover.background);

            _slotSelStyle = new GUIStyle()
            {
                normal  = { background = _slotSelTex, textColor = Color.clear },
                hover   = { background = _slotSelTex, textColor = Color.clear },
                active  = { background = _slotSelTex, textColor = Color.clear },
                padding = new RectOffset()
            };

            var installBg = MakeTex(2, 2, new Color(0.10f, 0.40f, 0.15f, 0.95f));
            UnityEngine.Object.DontDestroyOnLoad(installBg);
            _installBtnStyle = new GUIStyle()
            {
                normal  = { background = installBg, textColor = Color.white },
                fontSize = 12
            };

            var removeBg = MakeTex(2, 2, new Color(0.40f, 0.12f, 0.10f, 0.95f));
            UnityEngine.Object.DontDestroyOnLoad(removeBg);
            _removeBtnStyle = new GUIStyle()
            {
                normal  = { background = removeBg, textColor = Color.white },
                fontSize = 12
            };

            _stylesReady = true;
        }

        private static Texture2D MakeTex(int w, int h, Color c)
        {
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = c;
            var t = new Texture2D(w, h);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        private static Color StatusColor(ComponentStatus s)
        {
            return s switch
            {
                ComponentStatus.Ok       => ColOk,
                ComponentStatus.Degraded => ColDegraded,
                ComponentStatus.Failed   => ColFailed,
                _                        => ColEmpty
            };
        }
    }
}
