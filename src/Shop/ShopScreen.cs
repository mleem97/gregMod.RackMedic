using System;
using System.Collections.Generic;
using System.Linq;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using RackMedic.Core;
using Il2CppInterop.Runtime.Attributes;

namespace RackMedic.Shop
{
    /// <summary>
    /// Full-screen IMGUI shop (default key: F8).
    /// Tabs: All | Chassis | CPU | RAM | PSU | NIC | Storage | Cooling | UPS | Network
    /// Search bar + sort.
    /// Grid of items with Buy button.
    /// Bottom inventory summary.
    /// </summary>
    public class ShopScreen : MonoBehaviour
    {
        public ShopScreen(IntPtr ptr) : base(ptr) { }

        // ── Singleton ─────────────────────────────────────────────────────────
        public static ShopScreen Instance { get; private set; }

        // ── State ─────────────────────────────────────────────────────────────
        private bool   _open;
        private int    _activeTab   = 0;   // 0=All 1=Chassis 2=CPU 3=RAM 4=PSU 5=NIC 6=Storage 7=Cooling 8=UPS 9=Network
        private string _search      = "";
        private int    _sortMode    = 0;   // 0=Price↑ 1=Price↓ 2=Name
        private Vector2 _gridScroll;
        private Vector2 _invScroll;
        private bool   _reenableNextFrame;
        private string _feedbackMsg = "";
        private float  _feedbackExpiry;

        // ── Client demand state ─────────────────────────────────────────
        private Vector2            _clientScroll;
        private float              _clientsRefreshTime = -99f;
        private List<ClientDemand> _clientDemands      = new();

        // ── Layout ────────────────────────────────────────────────────────────
        private const float WIN_W = 920f;
        private const float WIN_H = 620f;
        private Rect _winRect;
        private const float CARD_W = 195f;
        private const float CARD_H = 170f;
        private const float SIDEBAR_W = 140f;   // category sidebar

        // ── Tabs ─────────────────────────────────────────────────────────────
        private const int CLIENTS_TAB = 10;
        private static readonly string[] TabNames =
            { "All", "Chassis", "CPU", "RAM", "PSU", "NIC", "Storage", "Cooling", "UPS", "Network", "Clients" };

        // ── Sort labels ───────────────────────────────────────────────────────
        private static readonly string[] SortLabels = { "Price ↑", "Price ↓", "Name" };

        // ── Cached item lists ─────────────────────────────────────────────────
        private List<ShopItem> _filtered = new();
        private string _lastSearch = null;
        private int    _lastTab    = -1;
        private int    _lastSort   = -1;

        // ── GUIStyles (lazy) ─────────────────────────────────────────────────
        private bool       _stylesReady;
        private GUIStyle   _winStyle, _titleStyle, _tabStyle, _tabActiveStyle;
        private GUIStyle   _cardNameStyle, _cardDescStyle, _cardPriceStyle;
        private GUIStyle   _buyBtnStyle, _buyGreenStyle;
        private GUIStyle   _labelStyle, _dimStyle, _searchStyle;
        private GUIStyle   _tierBadgeStyle;
        private GUIStyle   _sidebarBtnStyle, _sidebarActiveBtnStyle;
        private Texture2D  _bgTex;
        private Texture2D  _cardBg, _cardHoverBg;
        private Texture2D[] _tierBadgeBg;
        private int[]      _tabCounts;   // cached count per tab index

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            Instance = this;
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
                if (!_open && e.keyCode == RackMedicMod.ShopKey)
                { Open(); e.Use(); return; }
                if (_open && e.keyCode == KeyCode.Escape)
                { Close(); e.Use(); return; }

                // Search bar text input – handled here to avoid GUI.TextField which calls
                // TextEditor.SaveBackup() which is stripped in this IL2Cpp build.
                if (_open)
                {
                    if (e.keyCode == KeyCode.Backspace)
                    {
                        if (_search.Length > 0) { _search = _search.Substring(0, _search.Length - 1); _lastTab = -1; }
                        e.Use();
                    }
                    else if (e.character != '\0' && !char.IsControl(e.character) && _search.Length < 64)
                    {
                        _search += e.character;
                        _lastTab = -1;
                        e.Use();
                    }
                }
            }
            if (!_open) return;
            EnsureStyles();

            // Dim overlay
            GUI.color = new Color(0, 0, 0, 0.60f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _bgTex);
            GUI.color = Color.white;

            GUI.Box(_winRect, string.Empty, _winStyle);
            GUI.BeginGroup(_winRect);
            DrawWindow();
            GUI.EndGroup();
        }

        // ── Window drawing ──────────────────────────────────────────────────────

        private void DrawWindow()
        {
            const float pad = 10f;
            float y = pad;

            // ── Title + balance + close ───────────────────────────────────────
            GUI.Label(new Rect(pad, y, 300f, 28f), "RackMedic Shop", _titleStyle);

            int coins = GetCoins();
            GUI.Label(new Rect(WIN_W - 230f, y + 4f, 220f, 22f),
                $"Balance:  {coins:N0} ₵", _labelStyle);

            if (GUI.Button(new Rect(WIN_W - pad - 80f, y, 80f, 28f), "Close"))
            { Close(); return; }

            y += 34f;

            // ── Feedback message ──────────────────────────────────────────────
            if (!string.IsNullOrEmpty(_feedbackMsg) && Time.realtimeSinceStartup < _feedbackExpiry)
            {
                GUI.color = new Color(0.25f, 0.9f, 0.25f);
                GUI.Label(new Rect(pad, y - 8f, WIN_W - 2 * pad, 20f), _feedbackMsg, _dimStyle);
                GUI.color = Color.white;
            }

            // ── Tabs ──────────────────────────────────────────────────────────
            // (sidebar — drawn below together with the grid))

            // ── Search + sort row (components only — hidden on Clients tab) ───
            if (_activeTab != CLIENTS_TAB)
            {
                GUI.Label(new Rect(pad, y + 3f, 42f, 20f), "Search:", _dimStyle);
                // Manual text-box: avoids GUI.TextField which crashes (TextEditor.SaveBackup stripped).
                var searchBoxR = new Rect(pad + 46f, y, 260f, 22f);
                GUI.Box(searchBoxR, string.Empty, _searchStyle);
                string searchDisplay = _search.Length > 0 ? _search + "|" : "type to filter...|";
                GUI.Label(new Rect(searchBoxR.x + 4f, searchBoxR.y + 2f, searchBoxR.width - 8f, 18f),
                          searchDisplay, _dimStyle);

                GUI.Label(new Rect(pad + 326f, y + 3f, 34f, 20f), "Sort:", _dimStyle);
                for (int i = 0; i < SortLabels.Length; i++)
                {
                    bool active = _sortMode == i;
                    if (GUI.Button(new Rect(pad + 364f + i * 80f, y, 76f, 22f),
                                   SortLabels[i],
                                   active ? _tabActiveStyle : _tabStyle))
                    {
                        _sortMode = i;
                        _lastTab  = -1;
                    }
                }
                y += 28f;
            }

            // ── Divider ───────────────────────────────────────────────────────
            GUI.color = new Color(0.30f, 0.30f, 0.35f);
            GUI.DrawTexture(new Rect(pad, y, WIN_W - 2 * pad, 1f), _bgTex);
            GUI.color = Color.white;
            y += 6f;

            // ── Item grid (with sidebar) ─────────────────────────────────────────────
            float gridH = WIN_H - y - 80f;
            EnsureTabCounts();
            DrawCategoryBar(new Rect(pad, y, SIDEBAR_W, gridH));
            if (_activeTab == CLIENTS_TAB)
                DrawClientsPanel(new Rect(pad + SIDEBAR_W + 5f, y, WIN_W - pad * 2f - SIDEBAR_W - 5f, gridH));
            else
            {
                RefreshFiltered();
                DrawGrid(new Rect(pad + SIDEBAR_W + 5f, y, WIN_W - pad * 2f - SIDEBAR_W - 5f, gridH));
            }
            y += gridH + 6f;

            // ── Divider ───────────────────────────────────────────────────────
            GUI.color = new Color(0.30f, 0.30f, 0.35f);
            GUI.DrawTexture(new Rect(pad, y, WIN_W - 2 * pad, 1f), _bgTex);
            GUI.color = Color.white;
            y += 5f;

            // ── Inventory summary bar ─────────────────────────────────────────
            DrawInventoryBar(new Rect(pad, y, WIN_W - 2 * pad, 60f));

        }

        // ── Category sidebar ─────────────────────────────────────────────────

        private void DrawCategoryBar(Rect r)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.04f);
            GUI.DrawTexture(r, _bgTex);
            GUI.color = Color.white;

            float y = r.y + 6f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                int count = _tabCounts != null && i < _tabCounts.Length ? _tabCounts[i] : 0;
                if (count == 0 && i != 0 && i != CLIENTS_TAB) continue;

                string label  = i == CLIENTS_TAB ? "Clients" : $"{TabNames[i]}  ({count})";
                bool   active = _activeTab == i;

                if (GUI.Button(new Rect(r.x + 4f, y, r.width - 8f, 28f), label,
                               active ? _sidebarActiveBtnStyle : _sidebarBtnStyle))
                {
                    _activeTab  = i;
                    _lastTab    = -1;
                    _gridScroll = Vector2.zero;
                }
                y += 32f;
            }
        }

        private void EnsureTabCounts()
        {
            if (_tabCounts != null) return;
            _tabCounts = new int[TabNames.Length];
            var all = GetAllItems();
            _tabCounts[0] = all.Count;
            foreach (var item in all)
                if (item.TabIndex >= 1 && item.TabIndex < _tabCounts.Length)
                    _tabCounts[item.TabIndex]++;
        }

        // ── Item grid ─────────────────────────────────────────────────────────

        private void DrawGrid(Rect area)
        {
            int cols      = Mathf.Max(1, Mathf.FloorToInt((area.width - 16f) / (CARD_W + 10f)));
            int count     = _filtered.Count;
            int rows      = count == 0 ? 1 : Mathf.CeilToInt((float)count / cols);
            float totalH  = rows * (CARD_H + 8f);
            var contentR  = new Rect(0, 0, area.width - 16f, totalH);

            _gridScroll = GUI.BeginScrollView(area, _gridScroll, contentR);

            if (count == 0)
                GUI.Label(new Rect(8f, 8f, area.width - 30f, 24f), "No items.", _dimStyle);

            for (int i = 0; i < count; i++)
            {
                int col  = i % cols;
                int row  = i / cols;
                DrawCard(new Rect(col * (CARD_W + 10f), row * (CARD_H + 8f), CARD_W, CARD_H), _filtered[i]);
            }

            GUI.EndScrollView();
        }

        [HideFromIl2Cpp]
        private void DrawCard(Rect r, ShopItem item)
        {
            // Card background
            GUI.DrawTexture(r, _cardBg);

            float pad = 8f;
            float x   = r.x + pad;
            float y   = r.y + pad;
            float iw  = r.width - pad * 2;

            // Tier badge (top-right)
            if (item.Tier > 0)
            {
                var badgeTex = _tierBadgeBg[Mathf.Clamp(item.Tier - 1, 0, 2)];
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(r.xMax - 28f, r.y + 5f, 24f, 14f), badgeTex);
                GUI.color = Color.white;
                GUI.Label(new Rect(r.xMax - 27f, r.y + 3f, 24f, 14f),
                    item.Tier == 1 ? "G1" : item.Tier == 2 ? "G2" : "G3",
                    _tierBadgeStyle);
            }

            // Coloured type indicator bar (left edge)
            GUI.color = TypeColor(item.ComponentType);
            GUI.DrawTexture(new Rect(r.x, r.y, 4f, r.height), _bgTex);
            GUI.color = Color.white;

            // Item name
            GUI.Label(new Rect(x + 4f, y, iw - 8f, 22f), item.DisplayName, _cardNameStyle);
            y += 22f;

            // Type label
            GUI.Label(new Rect(x + 4f, y, iw - 8f, 16f), item.TypeLabel, _dimStyle);
            y += 18f;

            // Description (up to 2 lines)
            float descH = 32f;
            GUI.Label(new Rect(x + 4f, y, iw - 8f, descH), item.Description, _cardDescStyle);
            y += descH + 4f;

            // Stat snippet
            if (!string.IsNullOrEmpty(item.StatLine))
            {
                GUI.Label(new Rect(x + 4f, y, iw - 8f, 16f), item.StatLine, _dimStyle);
                y += 18f;
            }

            // Price
            y = r.y + r.height - 36f;
            GUI.Label(new Rect(x + 4f, y, 80f, 20f), $"{item.Price:N0} ₵", _cardPriceStyle);

            // Stock in inventory
            int stock = ComponentStore.Instance?.GetStock(item.Id) ?? 0;
            if (stock > 0)
            {
                GUI.color = new Color(0.4f, 0.9f, 0.4f);
                GUI.Label(new Rect(x + 85f, y + 2f, 40f, 16f), $"×{stock}", _dimStyle);
                GUI.color = Color.white;
            }

            // Buy button
            int coins = GetCoins();
            bool canAfford = coins >= item.Price;
            GUI.color = canAfford ? Color.white : new Color(1f, 0.4f, 0.4f);
            if (GUI.Button(new Rect(r.x + r.width - 52f, y - 2f, 48f, 26f), "Buy",
                           canAfford ? _buyGreenStyle : _buyBtnStyle))
            {
                if (canAfford)
                    TryBuyItem(item);
            }
            GUI.color = Color.white;
        }

        // ── Inventory bar ─────────────────────────────────────────────────────

        private void DrawInventoryBar(Rect area)
        {
            GUI.Label(new Rect(area.x, area.y, 90f, 18f), "Inventory:", _labelStyle);

            var inv = ComponentStore.Instance?.Inventory;
            if (inv == null || inv.Count == 0)
            {
                GUI.Label(new Rect(area.x + 94f, area.y, area.width - 94f, 18f), "empty", _dimStyle);
                return;
            }

            // Summarise by type
            var summary = new Dictionary<string, int>();
            foreach (var kv in inv)
            {
                if (kv.Value <= 0) continue;
                var def = ShopCatalog.FindComponent(kv.Key);
                string key = def != null ? def.Type.ToString() : "Other";
                summary.TryGetValue(key, out int cur);
                summary[key] = cur + kv.Value;
            }

            float x = area.x + 94f;
            foreach (var kv in summary)
            {
                string text = $"{kv.Key}(×{kv.Value})";
                GUI.Label(new Rect(x, area.y, 110f, 18f), text, _dimStyle);
                x += 115f;
                if (x + 115f > area.xMax) break;
            }

            // Total items
            int total = inv.Values.Sum();
            GUI.Label(new Rect(area.xMax - 100f, area.y, 96f, 18f),
                $"Total: {total}", _dimStyle);
        }

        // ── Filter / sort ─────────────────────────────────────────────────────

        private void RefreshFiltered()
        {
            bool dirty = _activeTab != _lastTab
                      || _search    != _lastSearch
                      || _sortMode  != _lastSort;
            if (!dirty) return;

            _lastTab    = _activeTab;
            _lastSearch = _search;
            _lastSort   = _sortMode;

            var all = GetAllItems();
            string q = _search.ToLowerInvariant().Trim();

            _filtered = all
                .Where(item =>
                {
                    // Tab filter
                    if (_activeTab != 0 && item.TabIndex != _activeTab) return false;
                    // Search
                    if (!string.IsNullOrEmpty(q))
                        if (!item.DisplayName.ToLower().Contains(q) &&
                            !item.Description.ToLower().Contains(q) &&
                            !item.TypeLabel.ToLower().Contains(q))
                            return false;
                    return true;
                })
                .OrderBy(item =>
                {
                    return _sortMode switch
                    {
                        1 => -item.Price,
                        2 => 0,         // name sort handled separately
                        _ => item.Price
                    };
                })
                .ThenBy(item => _sortMode == 2 ? item.DisplayName : item.DisplayName)
                .ToList();
        }

        private static List<ShopItem> GetAllItems()
        {
            var list = new List<ShopItem>();

            // Components (tab 2–7)
            foreach (var def in ShopCatalog.Components)
            {
                list.Add(new ShopItem
                {
                    Id            = def.Id,
                    DisplayName   = def.DisplayName,
                    Description   = def.Description,
                    TypeLabel     = def.Type.ToString(),
                    Price         = def.Price,
                    Tier          = (int)def.Tier,
                    StatLine      = MakeStatLine(def),
                    TabIndex      = ComponentTypeToTab(def.Type),
                    ComponentType = def.Type
                });
            }

            // UPS (tab 8)
            foreach (var ups in ShopCatalog.UpsUnits)
            {
                list.Add(new ShopItem
                {
                    Id          = ups.Id,
                    DisplayName = ups.DisplayName,
                    Description = ups.Description,
                    TypeLabel   = "UPS",
                    Price       = ups.Price,
                    Tier        = 0,
                    StatLine    = $"{ups.CapacityVa}VA  |  {ups.MaxProtectedServers} servers  |  {ups.RuntimeMinutes}min",
                    TabIndex    = 8,
                });
            }

            // Chassis (tab 1)
            foreach (var ch in ShopCatalog.Chassis)
            {
                list.Add(new ShopItem
                {
                    Id          = ch.Id,
                    DisplayName = ch.DisplayName,
                    Description = ch.Description,
                    TypeLabel   = "Chassis",
                    Price       = ch.Price,
                    Tier        = 0,
                    StatLine    = $"{ch.SizeInU}U  |  {ch.CpuSlots}CPU/{ch.RamSlots}RAM/{ch.StorageSlots}HDD",
                    TabIndex    = 1,
                });
            }

            return list;
        }

        private static string MakeStatLine(ComponentDef def)
        {
            return def.Type switch
            {
                ComponentType.CPU          => $"+{def.ProcessingSpeedBonus:F0}% proc speed",
                ComponentType.RAM          => $"{def.RamCapacityGb:F0} GB",
                ComponentType.PSU          => $"{def.PowerWatts:F0} W",
                ComponentType.NIC          => $"{def.NetworkSpeedGbps:F0} GbE",
                ComponentType.StorageDrive => def.IsNvme ? $"{def.StorageCapacityTb:F1} TB NVMe"
                                                         : $"{def.StorageCapacityTb:F1} TB",
                ComponentType.Cooling      => $"Eff: {def.CoolingEfficiency:F2}×",
                _                          => string.Empty
            };
        }

        private static int ComponentTypeToTab(ComponentType t)
        {
            return t switch
            {
                ComponentType.CPU          => 2,
                ComponentType.RAM          => 3,
                ComponentType.PSU          => 4,
                ComponentType.NIC          => 5,
                ComponentType.StorageDrive => 6,
                ComponentType.Cooling      => 7,
                _                          => 0
            };
        }

        // ── Client demand panel ───────────────────────────────────────────────

        private void DrawClientsPanel(Rect area)
        {
            // Auto-refresh every 10 real seconds
            if (Time.realtimeSinceStartup - _clientsRefreshTime > 10f)
                RefreshClientDemands();

            // Header row
            float y = area.y + 6f;
            GUI.Label(new Rect(area.x + 8f, y, 300f, 20f), "Active Client Demands", _titleStyle);
            if (GUI.Button(new Rect(area.xMax - 88f, y, 82f, 22f), "Refresh", _tabStyle))
                RefreshClientDemands();
            y += 30f;

            if (_clientDemands.Count == 0)
            {
                GUI.Label(new Rect(area.x + 8f, y, area.width - 16f, 60f),
                    "No active clients found.\n" +
                    "Clients will appear here once they have been assigned servers in your data center.",
                    _dimStyle);
                return;
            }

            const float ROW_H = 96f;
            float contentH = _clientDemands.Count * (ROW_H + 6f);
            var viewR = new Rect(area.x, y, area.width, area.height - (y - area.y));
            _clientScroll = GUI.BeginScrollView(viewR, _clientScroll,
                new Rect(0, 0, viewR.width - 16f, contentH));

            float iy = 0f;
            foreach (var demand in _clientDemands)
            {
                DrawClientRow(new Rect(4f, iy, viewR.width - 20f, ROW_H), demand);
                iy += ROW_H + 6f;
            }

            GUI.EndScrollView();
        }

        private void DrawClientRow(Rect r, ClientDemand d)
        {
            // Card background
            GUI.color = new Color(0.13f, 0.13f, 0.16f, 1f);
            GUI.DrawTexture(r, _bgTex);
            GUI.color = Color.white;

            // Left accent bar (CPU colour)
            GUI.color = TypeColor(ComponentType.CPU);
            GUI.DrawTexture(new Rect(r.x, r.y, 4f, r.height), _bgTex);
            GUI.color = Color.white;

            float x = r.x + 12f;
            float y = r.y + 8f;
            float w = r.width - 20f;

            // ── Client name ────────────────────────────────────────────────────
            GUI.Label(new Rect(x, y, w, 20f), d.Name, _cardNameStyle);
            y += 22f;

            // ── Speed metrics ─────────────────────────────────────────────────
            bool hasReq = d.RequiredTotal > 0f;
            bool met    = d.CurrentTotal >= d.RequiredTotal;
            float pct   = hasReq ? Mathf.Clamp01(d.CurrentTotal / d.RequiredTotal) : 1f;

            string statusText;
            if (!hasReq)
                statusText = "No speed requirements";
            else if (met)
                statusText = $"\u2713 Met  (need {d.RequiredTotal:F1}, have {d.CurrentTotal:F1})";
            else
                statusText = $"\u2193 Below by {(1f - pct) * 100f:F0}%  " +
                             $"(need {d.RequiredTotal:F1}, have {d.CurrentTotal:F1})";

            GUI.color = !hasReq  ? new Color(0.55f, 0.55f, 0.60f)
                      : met      ? new Color(0.25f, 0.85f, 0.25f)
                      :            new Color(1.0f,  0.45f, 0.25f);
            GUI.Label(new Rect(x, y, w, 18f), statusText, _dimStyle);
            GUI.color = Color.white;
            y += 20f;

            // ── Speed bar ─────────────────────────────────────────────────────
            if (hasReq)
            {
                float barW = w;
                GUI.color = new Color(0.20f, 0.20f, 0.24f);
                GUI.DrawTexture(new Rect(x, y, barW, 7f), _bgTex);
                GUI.color = met ? new Color(0.22f, 0.72f, 0.22f) : new Color(0.75f, 0.35f, 0.12f);
                GUI.DrawTexture(new Rect(x, y, barW * pct, 7f), _bgTex);
                GUI.color = Color.white;
                y += 12f;
            }

            // ── Component recommendation ──────────────────────────────────────
            if (hasReq && !met)
            {
                // Recommend the best CPU from the catalog (highest speed bonus)
                var bestCpu = ShopCatalog.Components
                    .Where(c => c.Type == ComponentType.CPU)
                    .OrderByDescending(c => c.ProcessingSpeedBonus)
                    .FirstOrDefault();

                if (bestCpu != null)
                {
                    GUI.color = TypeColor(ComponentType.CPU);
                    GUI.Label(new Rect(x, y, w, 18f),
                        $"Recommended: {bestCpu.DisplayName}  " +
                        $"(+{bestCpu.ProcessingSpeedBonus * 100f:F0}% proc speed \u2022 {bestCpu.Price:N0} \u20b5)",
                        _dimStyle);
                    GUI.color = Color.white;
                }
            }
        }

        private void RefreshClientDemands()
        {
            _clientsRefreshTime = Time.realtimeSinceStartup;
            _clientDemands.Clear();

            try
            {
                var bases = UnityEngine.Object.FindObjectsOfType<Il2Cpp.CustomerBase>();
                foreach (var cb in bases)
                {
                    try
                    {
                        // customerName lives on the linked CustomerItem
                        string name = cb.customerItem?.customerName ?? "Unknown Client";

                        // maximumAppRequirementsSpeedTotal = sum of all per-app speed requirements
                        float reqTotal = cb.maximumAppRequirementsSpeedTotal;
                        // currentSpeed = processing speed currently being delivered to this customer
                        float curr = cb.currentSpeed;

                        _clientDemands.Add(new ClientDemand
                        {
                            Name          = name,
                            RequiredTotal = reqTotal,
                            CurrentTotal  = curr,
                        });
                    }
                    catch { /* skip individual bad entries */ }
                }

                MelonLogger.Msg($"[RackMedic] Client demands refreshed: {_clientDemands.Count} clients.");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] RefreshClientDemands: {ex.Message}");
            }
        }

        // ── Buy logic ─────────────────────────────────────────────────────────

        [HideFromIl2Cpp]
        private void TryBuyItem(ShopItem item)
        {
            int currentCoins = GetCoins();
            if (currentCoins < item.Price)
            {
                ShowFeedback($"Not enough coins (need {item.Price:N0} ₵)");
                return;
            }

            if (!SpendCoins(item.Price))
            {
                ShowFeedback("Purchase failed");
                return;
            }

            ComponentStore.Instance?.AddToInventory(item.Id, 1);
            ShowFeedback($"Bought: {item.DisplayName}  (−{item.Price:N0} ₵)");
            MelonLogger.Msg($"[RackMedic] Purchased {item.Id} for {item.Price} coins");
        }

        private static int GetCoins() => GameAccess.GetCoins();

        private static bool SpendCoins(int amount) => GameAccess.SpendCoins(amount);

        private void ShowFeedback(string msg)
        {
            _feedbackMsg    = msg;
            _feedbackExpiry = Time.realtimeSinceStartup + 3f;
        }

        // ── Open / Close ──────────────────────────────────────────────────────

        [HideFromIl2Cpp]
        public void Open()
        {
            _open       = true;
            _lastTab    = -1; // force refresh
            _lastSearch = null;
            _lastSort   = -1;
            _gridScroll = Vector2.zero;
            _invScroll  = Vector2.zero;
            DisableInput();
        }

        private void Close()
        {
            _open = false;
            _reenableNextFrame = true;
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private static void DisableInput()
        {
            try
            {
                InputSystem.DisableDevice(Keyboard.current);
                InputSystem.DisableDevice(Mouse.current);
            }
            catch { }
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

        // ── Style init ────────────────────────────────────────────────────────

        private static Color TypeColor(ComponentType? t)
        {
            return t switch
            {
                ComponentType.CPU          => new Color(0.35f, 0.65f, 1.0f),
                ComponentType.RAM          => new Color(0.40f, 0.90f, 0.40f),
                ComponentType.PSU          => new Color(1.0f,  0.75f, 0.20f),
                ComponentType.NIC          => new Color(0.70f, 0.35f, 1.0f),
                ComponentType.StorageDrive => new Color(0.80f, 0.50f, 0.20f),
                ComponentType.Cooling      => new Color(0.20f, 0.85f, 0.90f),
                _                          => new Color(0.55f, 0.55f, 0.55f)
            };
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _bgTex = MakeTex(2, 2, Color.white);
            UnityEngine.Object.DontDestroyOnLoad(_bgTex);

            var winBg = MakeTex(2, 2, new Color(0.07f, 0.07f, 0.09f, 0.98f));
            UnityEngine.Object.DontDestroyOnLoad(winBg);
            _winStyle = new GUIStyle()
            {
                normal  = { background = winBg },
                padding = new RectOffset()
            };

            _cardBg = MakeTex(2, 2, new Color(0.13f, 0.13f, 0.16f, 1f));
            UnityEngine.Object.DontDestroyOnLoad(_cardBg);

            _cardHoverBg = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f, 1f));
            UnityEngine.Object.DontDestroyOnLoad(_cardHoverBg);

            // Tier badge backgrounds: Gen1=blue, Gen2=green, Gen3=gold
            _tierBadgeBg = new[]
            {
                MakeTex(2, 2, new Color(0.22f, 0.35f, 0.70f)),
                MakeTex(2, 2, new Color(0.15f, 0.55f, 0.25f)),
                MakeTex(2, 2, new Color(0.70f, 0.55f, 0.10f)),
            };
            foreach (var t in _tierBadgeBg) UnityEngine.Object.DontDestroyOnLoad(t);

            var tabBg  = MakeTex(2, 2, new Color(0.18f, 0.18f, 0.22f));
            var tabActBg = MakeTex(2, 2, new Color(0.25f, 0.45f, 0.75f));
            UnityEngine.Object.DontDestroyOnLoad(tabBg);
            UnityEngine.Object.DontDestroyOnLoad(tabActBg);

            _tabStyle = new GUIStyle()
            {
                normal   = { background = tabBg,    textColor = new Color(0.75f, 0.75f, 0.75f) },
                hover    = { background = tabActBg,  textColor = Color.white },
                fontSize = 11
            };
            _tabActiveStyle = new GUIStyle()
            {
                normal    = { background = tabActBg, textColor = Color.white },
                hover     = { background = tabActBg, textColor = Color.white },
                fontSize  = 11,
                fontStyle = FontStyle.Bold
            };

            _titleStyle = new GUIStyle()
            {
                fontSize  = 18,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white }
            };

            _labelStyle = new GUIStyle()
            {
                fontSize = 12,
                normal   = { textColor = Color.white }
            };

            _dimStyle = new GUIStyle()
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.65f, 0.65f, 0.65f) },
                richText = true
            };

            _searchStyle = new GUIStyle()
            {
                fontSize = 12,
                normal   = { textColor = Color.white }
            };

            _cardNameStyle = new GUIStyle()
            {
                fontSize  = 12,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = Color.white },
                wordWrap  = true
            };

            _cardDescStyle = new GUIStyle()
            {
                fontSize = 10,
                normal   = { textColor = new Color(0.65f, 0.65f, 0.65f) },
                wordWrap = true
            };

            _cardPriceStyle = new GUIStyle()
            {
                fontSize  = 13,
                fontStyle = FontStyle.Bold,
                normal    = { textColor = new Color(0.95f, 0.85f, 0.25f) }
            };

            var buyBg      = MakeTex(2, 2, new Color(0.22f, 0.22f, 0.25f));
            var buyGreenBg = MakeTex(2, 2, new Color(0.10f, 0.42f, 0.15f));
            UnityEngine.Object.DontDestroyOnLoad(buyBg);
            UnityEngine.Object.DontDestroyOnLoad(buyGreenBg);

            _buyBtnStyle = new GUIStyle()
            {
                fontSize = 11,
                normal   = { background = buyBg, textColor = Color.white }
            };
            _buyGreenStyle = new GUIStyle()
            {
                fontSize = 11,
                normal   = { background = buyGreenBg, textColor = Color.white },
                active   = { background = buyBg,      textColor = Color.white }
            };

            _tierBadgeStyle = new GUIStyle()
            {
                fontSize  = 9,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            };

            // Sidebar styles
            var sidebarNormBg = MakeTex(2, 2, new Color(0.14f, 0.14f, 0.17f));
            var sidebarActBg  = MakeTex(2, 2, new Color(0.04f, 0.64f, 0.75f));
            var sidebarHovBg  = MakeTex(2, 2, new Color(0.20f, 0.20f, 0.26f));
            UnityEngine.Object.DontDestroyOnLoad(sidebarNormBg);
            UnityEngine.Object.DontDestroyOnLoad(sidebarActBg);
            UnityEngine.Object.DontDestroyOnLoad(sidebarHovBg);

            var sidePad = new RectOffset();
            sidePad.left = 8;
            sidePad.right = 4;
            sidePad.top = 2;
            sidePad.bottom = 2;

            _sidebarBtnStyle = new GUIStyle()
            {
                fontSize  = 11,
                alignment = TextAnchor.MiddleLeft,
                padding   = sidePad,
                normal    = { background = sidebarNormBg, textColor = new Color(0.80f, 0.80f, 0.84f) },
                hover     = { background = sidebarHovBg,  textColor = Color.white },
                active    = { background = sidebarActBg,  textColor = Color.white },
            };
            _sidebarActiveBtnStyle = new GUIStyle()
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding   = sidePad,
                normal    = { background = sidebarActBg, textColor = new Color(0.02f, 0.07f, 0.12f) },
                hover     = { background = sidebarHovBg, textColor = Color.white },
                active    = { background = sidebarActBg, textColor = new Color(0.02f, 0.07f, 0.12f) },
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
    }

    // ── Internal shop item record ─────────────────────────────────────────────

    internal class ShopItem
    {
        public string        Id;
        public string        DisplayName;
        public string        Description;
        public string        TypeLabel;
        public int           Price;
        public int           Tier;          // 0 = no tier badge
        public string        StatLine;
        public int           TabIndex;      // which tab
        public ComponentType? ComponentType; // null for UPS/Chassis
    }

    // ── Client demand data ────────────────────────────────────────────────────

    internal struct ClientDemand
    {
        public string Name;
        public float  RequiredTotal;  // sum of per-app speed requirements
        public float  CurrentTotal;   // current processing speed being delivered
    }
}
