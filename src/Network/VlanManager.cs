using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using UnityEngine;

namespace RackMedic.Network
{
    /// <summary>
    /// Tracks VLAN tag assignments for server ports.
    /// Port keys use the same format as PortLabels: "{objectPath}:{portIndex}"
    ///
    /// VLAN IDs: 1–4094 per IEEE 802.1Q.
    /// Persists to UserData/RackMedic/vlans.json.
    /// </summary>
    public class VlanManager : MonoBehaviour
    {
        public static VlanManager Instance { get; private set; }

        private static string DataPath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "RackMedic", "vlans.json");

        /// <summary>Port key → VLAN ID (1–4094). Absent key = untagged (VLAN 1 implicit).</summary>
        private Dictionary<string, int> _vlans = new();

        private void Awake()
        {
            Instance = this;
            Load();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Returns the VLAN tag for a port, or 1 (untagged) if not set.</summary>
        public int GetVlan(string portKey) =>
            _vlans.TryGetValue(portKey, out int v) ? v : 1;

        /// <summary>Assign a VLAN tag to a port. Removes entry if vlanId is 1 (default).</summary>
        public void SetVlan(string portKey, int vlanId)
        {
            if (string.IsNullOrEmpty(portKey)) return;
            if (vlanId < 1 || vlanId > 4094)
            {
                MelonLogger.Warning($"[RackMedic] Invalid VLAN ID {vlanId} — must be 1–4094");
                return;
            }

            if (vlanId == 1)
                _vlans.Remove(portKey);
            else
                _vlans[portKey] = vlanId;

            Save();
        }

        /// <summary>Remove VLAN tag from a port (revert to untagged).</summary>
        public void ClearVlan(string portKey) => SetVlan(portKey, 1);

        /// <summary>All ports that have a non-default VLAN assignment.</summary>
        public IReadOnlyDictionary<string, int> AllAssignments => _vlans;

        // ── Persistence ───────────────────────────────────────────────────────

        private void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(DataPath);
                Directory.CreateDirectory(dir);
                File.WriteAllText(DataPath,
                    JsonConvert.SerializeObject(_vlans, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RackMedic] VlanManager save failed: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(DataPath))
                    _vlans = JsonConvert.DeserializeObject<Dictionary<string, int>>(
                        File.ReadAllText(DataPath)) ?? new();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"[RackMedic] VlanManager load failed: {ex.Message}");
                _vlans = new();
            }
        }
    }
}
