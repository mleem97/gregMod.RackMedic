using MelonLoader;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using RackMedic.Core;
using RackMedic.Shop;
using RackMedic.Workbench;
using RackMedic.Network;
using RackMedic.Power;

[assembly: MelonInfo(typeof(RackMedic.RackMedicMod), "gregMod.RackMedic", "1.0.0", "TeamGreg Modding")]
[assembly: MelonGame("Waseku", "Data Center")]

namespace RackMedic
{
    public class RackMedicMod : MelonMod
    {
        // ── Singleton ─────────────────────────────────────────────────────────
        public static RackMedicMod Instance { get; private set; }

        // ── Preferences ───────────────────────────────────────────────────────
        private MelonPreferences_Category _prefs;
        public static MelonPreferences_Entry<string>  ShopKeyEntry;
        public static MelonPreferences_Entry<string>  WorkbenchKeyEntry;
        public static MelonPreferences_Entry<float>   FailureCheckIntervalEntry;  // real seconds between failure rolls
        public static MelonPreferences_Entry<float>   MinFailureHoursEntry;       // minimum powered-on hours before any failure
        public static MelonPreferences_Entry<bool>    PatchEolEntry;              // disable game's native EOL breaking

        // ── Internal objects ─────────────────────────────────────────────────
        private HarmonyLib.Harmony _harmony;
        private GameObject _managerGO;

        // Parsed keycodes
        public static KeyCode ShopKey     = KeyCode.F8;
        public static KeyCode WorkbenchKey = KeyCode.F7;

        // ── MelonLoader lifecycle ─────────────────────────────────────────────

        public override void OnInitializeMelon()
        {
            Instance = this;

            // Register all MonoBehaviour subclasses with the IL2Cpp domain
            ClassInjector.RegisterTypeInIl2Cpp<ComponentStore>();
            ClassInjector.RegisterTypeInIl2Cpp<FailureEngine>();
            ClassInjector.RegisterTypeInIl2Cpp<ShopScreen>();
            ClassInjector.RegisterTypeInIl2Cpp<ModShopIntegration>();
            ClassInjector.RegisterTypeInIl2Cpp<WorkbenchUI>();
            ClassInjector.RegisterTypeInIl2Cpp<WorkbenchStation>();
            ClassInjector.RegisterTypeInIl2Cpp<VlanManager>();
            ClassInjector.RegisterTypeInIl2Cpp<PowerGrid>();

            // Preferences
            _prefs = MelonPreferences.CreateCategory("RackMedic");
            ShopKeyEntry             = _prefs.CreateEntry("ShopKey",             "F8",   "Key to open the RackMedic shop");
            WorkbenchKeyEntry        = _prefs.CreateEntry("WorkbenchKey",        "F7",   "Key to open the workbench panel");
            FailureCheckIntervalEntry= _prefs.CreateEntry("FailureCheckInterval", 60f,  "Seconds between failure probability rolls");
            MinFailureHoursEntry     = _prefs.CreateEntry("MinFailureHours",      2f,   "Minimum powered-on hours before components can fail");
            PatchEolEntry            = _prefs.CreateEntry("PatchEol",            true,  "Disable the game's built-in 4-hour EOL server breaking");

            RefreshKeys();

            // Harmony patches
            _harmony = new HarmonyLib.Harmony("com.tindolt.rackmedic");
            _harmony.PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
            MelonLogger.Msg("[RackMedic] Harmony patches applied.");

            MelonLogger.Msg($"[RackMedic] RackMedic 1.0.0 loaded. Shop: {ShopKey}  Workbench: {WorkbenchKey}");
        }

        public override void OnSceneWasInitialized(int buildIndex, string sceneName)
        {
            // Create manager GameObject on first game scene load
            if (_managerGO == null)
            {
                _managerGO = new GameObject("RackMedicManager");
                Object.DontDestroyOnLoad(_managerGO);

                _managerGO.AddComponent<ComponentStore>();
                _managerGO.AddComponent<FailureEngine>();
                _managerGO.AddComponent<ShopScreen>();
                _managerGO.AddComponent<ModShopIntegration>();
                _managerGO.AddComponent<WorkbenchUI>();
                _managerGO.AddComponent<WorkbenchStation>();
                _managerGO.AddComponent<VlanManager>();
                _managerGO.AddComponent<PowerGrid>();

                MelonLogger.Msg("[RackMedic] Manager systems initialised.");
            }
        }

        public override void OnDeinitializeMelon()
        {
            _harmony?.UnpatchSelf();
        }

        public static void RefreshKeys()
        {
            TryParseKey(ShopKeyEntry?.Value ?? "F8",      ref ShopKey,      KeyCode.F8);
            TryParseKey(WorkbenchKeyEntry?.Value ?? "F7", ref WorkbenchKey, KeyCode.F7);
        }

        private static void TryParseKey(string value, ref KeyCode target, KeyCode fallback)
        {
            try   { target = (KeyCode)System.Enum.Parse(typeof(KeyCode), value, true); }
            catch { target = fallback; }
        }
    }
}
