using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace RackMedic.Core
{
    public enum ComponentStatus { Ok, Degraded, Failed, Empty }

    /// <summary>
    /// Mutable runtime state for one installed component slot.
    /// </summary>
    [Serializable]
    public class ComponentSlot
    {
        public string DefId;          // null / empty = empty slot
        public float  Health;         // 0–100; 0 = failed
        public float  PoweredOnHours; // cumulative hours this slot has been powered on

        [JsonIgnore]
        public ComponentStatus Status =>
            string.IsNullOrEmpty(DefId) ? ComponentStatus.Empty :
            Health <= 0f               ? ComponentStatus.Failed  :
            Health < 30f               ? ComponentStatus.Degraded :
                                         ComponentStatus.Ok;

        [JsonIgnore]
        public ComponentDef Def => string.IsNullOrEmpty(DefId)
            ? null : ShopCatalog.FindComponent(DefId);

        public static ComponentSlot Empty() => new ComponentSlot { DefId = null, Health = 100f };
    }

    /// <summary>
    /// Complete component profile for one server managed by RackMedic.
    /// </summary>
    [Serializable]
    public class ServerProfile
    {
        public string ServerId;
        public string ChassisId;       // which chassis was used

        // ── Component slots ────────────────────────────────────────────────
        public ComponentSlot   Cpu;
        public ComponentSlot[] Ram;
        public ComponentSlot   Psu;
        public ComponentSlot   Nic;
        public ComponentSlot[] Storage;
        public ComponentSlot   Cooling;

        // ── Aggregate bookkeeping ──────────────────────────────────────────
        public float  TotalPoweredOnHours;
        public string LastUpdatedUtc;  // ISO-8601, set on save

        // ── Factory ───────────────────────────────────────────────────────
        public static ServerProfile Create(string serverId, ChassisDef chassis)
        {
            return new ServerProfile
            {
                ServerId  = serverId,
                ChassisId = chassis.Id,
                Cpu       = ComponentSlot.Empty(),
                Ram       = InitSlots(chassis.RamSlots),
                Psu       = ComponentSlot.Empty(),
                Nic       = ComponentSlot.Empty(),
                Storage   = InitSlots(chassis.StorageSlots),
                Cooling   = ComponentSlot.Empty(),
                TotalPoweredOnHours = 0f,
                LastUpdatedUtc = DateTime.UtcNow.ToString("o"),
            };
        }

        private static ComponentSlot[] InitSlots(int count)
        {
            var arr = new ComponentSlot[count];
            for (int i = 0; i < count; i++) arr[i] = ComponentSlot.Empty();
            return arr;
        }

        // ── Computed properties ────────────────────────────────────────────
        [JsonIgnore]
        public bool HasAnyCriticalFailure
        {
            get
            {
                if (Cpu    != null && Cpu.Status    == ComponentStatus.Failed) return true;
                if (Psu    != null && Psu.Status    == ComponentStatus.Failed) return true;
                if (Cooling!= null && Cooling.Status== ComponentStatus.Failed) return true;
                return false;
            }
        }

        [JsonIgnore]
        public bool AllComponentsHealthy
        {
            get
            {
                if (IsSlotBad(Cpu))     return false;
                if (IsSlotBad(Psu))     return false;
                if (IsSlotBad(Nic))     return false;
                if (IsSlotBad(Cooling)) return false;
                if (Ram    != null) foreach (var s in Ram)     if (IsSlotBad(s)) return false;
                if (Storage!= null) foreach (var s in Storage) if (IsSlotBad(s)) return false;
                return true;
            }
        }

        private static bool IsSlotBad(ComponentSlot s)
            => s != null && !string.IsNullOrEmpty(s.DefId) && s.Status == ComponentStatus.Failed;

        [JsonIgnore]
        public float TotalPowerDrawWatts
        {
            get
            {
                float w = 0f;
                AddDraw(ref w, Cpu); AddDraw(ref w, Psu); AddDraw(ref w, Nic); AddDraw(ref w, Cooling);
                if (Ram    != null) foreach (var s in Ram)     AddDraw(ref w, s);
                if (Storage!= null) foreach (var s in Storage) AddDraw(ref w, s);
                return w;
            }
        }

        private static void AddDraw(ref float total, ComponentSlot s)
        {
            if (s?.Def != null) total += s.Def.PowerWatts;
        }

        [JsonIgnore]
        public float CoolingMultiplier
            => (Cooling?.Def != null) ? Cooling.Def.CoolingEfficiency : 1.5f; // penalty if no cooling

        // ── Collect failed component display names ─────────────────────────
        public List<string> GetFailedNames()
        {
            var list = new List<string>();
            CheckSlot(Cpu,     "CPU",     list);
            CheckSlot(Psu,     "PSU",     list);
            CheckSlot(Nic,     "NIC",     list);
            CheckSlot(Cooling, "Cooling", list);
            if (Ram    != null) for (int i=0;i<Ram.Length;i++)     CheckSlot(Ram[i],     $"RAM-{i+1}", list);
            if (Storage!= null) for (int i=0;i<Storage.Length;i++) CheckSlot(Storage[i], $"Drive-{i+1}",list);
            return list;
        }

        private static void CheckSlot(ComponentSlot s, string label, List<string> list)
        {
            if (s != null && s.Status == ComponentStatus.Failed)
                list.Add(label);
        }
    }
}
