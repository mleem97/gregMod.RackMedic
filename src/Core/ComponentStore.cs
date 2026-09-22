using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;
using Il2CppInterop.Runtime.Attributes;

namespace RackMedic.Core
{
    /// <summary>
    /// Persistent store for all server profiles and player inventory.
    /// Sits on the RackMedicManager GameObject.
    /// </summary>
    public class ComponentStore : MonoBehaviour
    {
        public ComponentStore(IntPtr ptr) : base(ptr) { }

        public static ComponentStore Instance { get; private set; }

        // ── Paths ─────────────────────────────────────────────────────────
        private static string DataDir   => Path.Combine(MelonEnvironment.UserDataDirectory, "RackMedic");
        private static string ProfilesPath => Path.Combine(DataDir, "serverprofiles.json");
        private static string InventoryPath => Path.Combine(DataDir, "inventory.json");

        // ── Data ──────────────────────────────────────────────────────────
        // serverId → profile  (only servers registered with RackMedic)
        [HideFromIl2Cpp]
        public Dictionary<string, ServerProfile> Profiles { get; private set; } = new();

        // componentDefId → count owned
        [HideFromIl2Cpp]
        public Dictionary<string, int> Inventory { get; private set; } = new();

        // ── Unity lifecycle ────────────────────────────────────────────────
        public void Awake()
        {
            Instance = this;
            Directory.CreateDirectory(DataDir);
            LoadAll();
        }

        // ── Public API ────────────────────────────────────────────────────

        public bool IsManaged(string serverId)
            => !string.IsNullOrEmpty(serverId) && Profiles.ContainsKey(serverId);

        [HideFromIl2Cpp]
        public ServerProfile GetOrCreate(string serverId, string chassisId = "chassis_2u")
        {
            if (Profiles.TryGetValue(serverId, out var existing))
                return existing;

            var chassis = ShopCatalog.FindChassis(chassisId) ?? ShopCatalog.Chassis[1];
            var profile = ServerProfile.Create(serverId, chassis);
            Profiles[serverId] = profile;
            Save();
            return profile;
        }

        [HideFromIl2Cpp]
        public ServerProfile Get(string serverId)
        {
            Profiles.TryGetValue(serverId, out var p);
            return p;
        }

        // ── Inventory ─────────────────────────────────────────────────────

        public int GetStock(string defId)
        {
            Inventory.TryGetValue(defId, out int count);
            return count;
        }

        public void AddToInventory(string defId, int qty = 1)
        {
            Inventory.TryGetValue(defId, out int cur);
            Inventory[defId] = cur + qty;
            Save();
        }

        public bool ConsumeFromInventory(string defId, int qty = 1)
        {
            Inventory.TryGetValue(defId, out int cur);
            if (cur < qty) return false;
            Inventory[defId] = cur - qty;
            Save();
            return true;
        }

        // ── Persistence ───────────────────────────────────────────────────

        public void Save()
        {
            try
            {
                foreach (var p in Profiles.Values)
                    p.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

                File.WriteAllText(ProfilesPath,  JsonConvert.SerializeObject(Profiles,  Formatting.Indented));
                File.WriteAllText(InventoryPath, JsonConvert.SerializeObject(Inventory, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RackMedic] Save failed: {ex.Message}");
            }
        }

        private void LoadAll()
        {
            try
            {
                if (File.Exists(ProfilesPath))
                    Profiles = JsonConvert.DeserializeObject<Dictionary<string, ServerProfile>>(
                                   File.ReadAllText(ProfilesPath)) ?? new();

                if (File.Exists(InventoryPath))
                    Inventory = JsonConvert.DeserializeObject<Dictionary<string, int>>(
                                    File.ReadAllText(InventoryPath)) ?? new();

                MelonLogger.Msg($"[RackMedic] Loaded {Profiles.Count} server profile(s), " +
                                $"{Inventory.Count} inventory line(s).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RackMedic] Load failed: {ex.Message}");
                Profiles  = new();
                Inventory = new();
            }
        }
    }
}
