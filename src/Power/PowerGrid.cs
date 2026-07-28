using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;
using UnityEngine;
using RackMedic.Core;

namespace RackMedic.Power
{
    /// <summary>
    /// Tracks instantaneous power draw across all managed servers and alerts when
    /// a configurable circuit capacity is exceeded.
    ///
    /// Power draw is derived from ServerProfile.TotalPowerDrawWatts which sums
    /// each installed component's PowerWatts field.
    /// </summary>
    public class PowerGrid : MonoBehaviour
    {
        public static PowerGrid Instance { get; private set; }

        // Default circuit capacity in watts (will be configurable via preferences later)
        private const float DefaultCircuitCapacityW = 5000f;

        // Per-circuit capacity override map: circuitId → watts
        private Dictionary<string, float> _circuitCapacity = new();

        // Server → circuit assignment
        private Dictionary<string, string> _serverCircuit = new();

        private bool _wasOverCapacity;

        private void Awake()
        {
            Instance = this;
        }

        private void Update()
        {
            // Check power balance every ~5 real seconds
            if (Time.frameCount % 300 == 0)
                CheckPowerBalance();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Returns total power draw (watts) for all managed powered-on servers.</summary>
        public float GetTotalDrawWatts()
        {
            var store = ComponentStore.Instance;
            if (store == null) return 0f;

            float total = 0f;
            foreach (var profile in store.Profiles.Values)
                total += profile.TotalPowerDrawWatts;
            return total;
        }

        /// <summary>Assign a server to a named circuit.</summary>
        public void AssignToCircuit(string serverId, string circuitId)
        {
            _serverCircuit[serverId] = circuitId;
        }

        /// <summary>Set or override capacity for a named circuit.</summary>
        public void SetCircuitCapacity(string circuitId, float watts)
        {
            _circuitCapacity[circuitId] = watts;
        }

        /// <summary>Returns total draw for one named circuit.</summary>
        public float GetCircuitDrawWatts(string circuitId)
        {
            var store = ComponentStore.Instance;
            if (store == null) return 0f;

            float total = 0f;
            foreach (var kv in _serverCircuit)
            {
                if (kv.Value != circuitId) continue;
                var p = store.Get(kv.Key);
                if (p != null) total += p.TotalPowerDrawWatts;
            }
            return total;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private void CheckPowerBalance()
        {
            float totalW    = GetTotalDrawWatts();
            float capacityW = DefaultCircuitCapacityW;

            bool over = totalW > capacityW;
            if (over && !_wasOverCapacity)
                MelonLogger.Warning($"[RackMedic] Power alert: draw {totalW:F0}W exceeds circuit capacity {capacityW:F0}W");
            else if (!over && _wasOverCapacity)
                MelonLogger.Msg($"[RackMedic] Power normalised: draw {totalW:F0}W / {capacityW:F0}W");

            _wasOverCapacity = over;
        }
    }
}
