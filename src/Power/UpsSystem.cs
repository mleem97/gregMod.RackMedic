using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;
using RackMedic.Core;

namespace RackMedic.Power
{
    /// <summary>
    /// Tracks which UPS units are installed and which servers they protect.
    /// When grid power fails (future feature), protected servers stay online for
    /// the UPS's BackupMinutes rating.
    ///
    /// Scaffold — buy/place UPS via shop, assign it to servers in workbench.
    /// </summary>
    public class UpsSystem : MonoBehaviour
    {
        public static UpsSystem Instance { get; private set; }

        // UPS inventory: defId → list of installed unit records
        private List<UpsUnit> _units = new();

        private void Awake() => Instance = this;

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Place a purchased UPS into the power grid.</summary>
        public UpsUnit InstallUps(string upsDefId)
        {
            var def = ShopCatalog.FindUps(upsDefId);
            if (def == null)
            {
                MelonLogger.Warning($"[RackMedic] Unknown UPS def: {upsDefId}");
                return null;
            }

            var unit = new UpsUnit
            {
                UpsId     = Guid.NewGuid().ToString("N").Substring(0, 8),
                DefId     = upsDefId,
                Def       = def,
                Protected = new List<string>()
            };
            _units.Add(unit);
            MelonLogger.Msg($"[RackMedic] UPS installed: {def.DisplayName} (id={unit.UpsId})");
            return unit;
        }

        /// <summary>Assign a server to an existing UPS unit.</summary>
        public bool AssignServer(string upsId, string serverId)
        {
            var unit = FindUnit(upsId);
            if (unit == null) return false;

            if (unit.Protected.Count >= unit.Def.MaxProtectedServers)
            {
                MelonLogger.Warning($"[RackMedic] UPS {upsId} is full ({unit.Def.MaxProtectedServers} servers max)");
                return false;
            }

            if (!unit.Protected.Contains(serverId))
                unit.Protected.Add(serverId);

            return true;
        }

        /// <summary>Returns true if a server has UPS protection.</summary>
        public bool IsProtected(string serverId)
        {
            foreach (var unit in _units)
                if (unit.Protected.Contains(serverId)) return true;
            return false;
        }

        /// <summary>All installed UPS units (read-only snapshot).</summary>
        public IReadOnlyList<UpsUnit> Units => _units;

        private UpsUnit FindUnit(string upsId)
        {
            foreach (var u in _units)
                if (u.UpsId == upsId) return u;
            return null;
        }
    }

    // ── Data model ────────────────────────────────────────────────────────────

    public class UpsUnit
    {
        public string          UpsId;
        public string          DefId;
        public UpsDef          Def;
        public List<string>    Protected; // server IDs this UPS covers
    }
}
