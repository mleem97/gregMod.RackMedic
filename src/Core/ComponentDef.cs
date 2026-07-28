using System.Collections.Generic;
using UnityEngine;

namespace RackMedic.Core
{
    public enum ComponentType
    {
        CPU,
        RAM,
        PSU,
        NIC,
        StorageDrive,
        Cooling
    }

    public enum ComponentTier { Gen1 = 1, Gen2 = 2, Gen3 = 3 }

    /// <summary>
    /// Immutable definition of a component type + tier — the blueprint bought from the shop.
    /// </summary>
    public class ComponentDef
    {
        public string         Id;
        public string         DisplayName;
        public string         Description;
        public ComponentType  Type;
        public ComponentTier  Tier;
        public int            Price;            // in coins
        public float          BaseLifetimeHours;// mean hours to first failure at 100% health
        public float          DegradationPerHour;// health points lost per powered-on hour (0–100 scale)

        // ── Stat values (vary by type/tier) ──────────────────────────────────
        public float  ProcessingSpeedBonus;   // CPU: additive bonus to game's processing speed
        public int    RamCapacityGb;          // RAM: capacity
        public float  PowerWatts;             // PSU: wattage rating; others: draw
        public int    NetworkSpeedGbps;       // NIC: port speed
        public int    StorageCapacityTb;      // Storage: capacity in TB
        public bool   IsNvme;                 // Storage: SSD NVMe flag
        public float  CoolingEfficiency;      // Cooling: multiplier — reduces degradation rate of all other components

        // ── Display ───────────────────────────────────────────────────────────
        public string TierLabel  => Tier == ComponentTier.Gen3 ? "★★★"
                                  : Tier == ComponentTier.Gen2 ? "★★☆"
                                  :                              "★☆☆";
        public Color32 TierColor => Tier == ComponentTier.Gen3 ? new Color32(255, 200, 50,  255)
                                  : Tier == ComponentTier.Gen2 ? new Color32(180, 220, 255, 255)
                                  :                              new Color32(180, 180, 180, 255);
    }

    /// <summary>
    /// Static catalog of all components + chassis available in the RackMedic shop.
    /// </summary>
    public static class ShopCatalog
    {
        public static readonly List<ComponentDef> Components = new List<ComponentDef>
        {
            // ── CPU ──────────────────────────────────────────────────────────
            new ComponentDef {
                Id="cpu_g1", DisplayName="Intel Xeon E3", Description="Entry-level server CPU.",
                Type=ComponentType.CPU, Tier=ComponentTier.Gen1,
                Price=200, BaseLifetimeHours=8f,  DegradationPerHour=0.8f,
                ProcessingSpeedBonus=0.05f, PowerWatts=80f },
            new ComponentDef {
                Id="cpu_g2", DisplayName="Intel Xeon E5", Description="Mid-range server CPU.",
                Type=ComponentType.CPU, Tier=ComponentTier.Gen2,
                Price=400, BaseLifetimeHours=14f, DegradationPerHour=0.5f,
                ProcessingSpeedBonus=0.15f, PowerWatts=95f },
            new ComponentDef {
                Id="cpu_g3", DisplayName="Intel Xeon Scalable", Description="High-performance server CPU.",
                Type=ComponentType.CPU, Tier=ComponentTier.Gen3,
                Price=800, BaseLifetimeHours=24f, DegradationPerHour=0.3f,
                ProcessingSpeedBonus=0.30f, PowerWatts=120f },

            // ── RAM ──────────────────────────────────────────────────────────
            new ComponentDef {
                Id="ram_g1", DisplayName="16GB DDR4", Description="Standard server RAM.",
                Type=ComponentType.RAM, Tier=ComponentTier.Gen1,
                Price=50,  BaseLifetimeHours=15f, DegradationPerHour=0.4f,
                RamCapacityGb=16, PowerWatts=4f },
            new ComponentDef {
                Id="ram_g2", DisplayName="32GB DDR4", Description="High-density DDR4 module.",
                Type=ComponentType.RAM, Tier=ComponentTier.Gen2,
                Price=100, BaseLifetimeHours=20f, DegradationPerHour=0.3f,
                RamCapacityGb=32, PowerWatts=5f },
            new ComponentDef {
                Id="ram_g3", DisplayName="64GB DDR5", Description="Next-gen DDR5 module.",
                Type=ComponentType.RAM, Tier=ComponentTier.Gen3,
                Price=200, BaseLifetimeHours=28f, DegradationPerHour=0.2f,
                RamCapacityGb=64, PowerWatts=6f },

            // ── PSU ──────────────────────────────────────────────────────────
            new ComponentDef {
                Id="psu_g1", DisplayName="500W Standard PSU", Description="Basic 80+ certified PSU.",
                Type=ComponentType.PSU, Tier=ComponentTier.Gen1,
                Price=100, BaseLifetimeHours=10f, DegradationPerHour=0.6f,
                PowerWatts=500f },
            new ComponentDef {
                Id="psu_g2", DisplayName="750W 80+ Gold PSU", Description="Efficient gold-rated PSU.",
                Type=ComponentType.PSU, Tier=ComponentTier.Gen2,
                Price=200, BaseLifetimeHours=18f, DegradationPerHour=0.35f,
                PowerWatts=750f },
            new ComponentDef {
                Id="psu_g3", DisplayName="1000W 80+ Platinum PSU", Description="Premium redundant-capable PSU.",
                Type=ComponentType.PSU, Tier=ComponentTier.Gen3,
                Price=400, BaseLifetimeHours=30f, DegradationPerHour=0.2f,
                PowerWatts=1000f },

            // ── NIC ──────────────────────────────────────────────────────────
            new ComponentDef {
                Id="nic_g1", DisplayName="1GbE Network Card", Description="Dual-port 1 Gigabit NIC.",
                Type=ComponentType.NIC, Tier=ComponentTier.Gen1,
                Price=80,  BaseLifetimeHours=18f, DegradationPerHour=0.3f,
                NetworkSpeedGbps=1, PowerWatts=5f },
            new ComponentDef {
                Id="nic_g2", DisplayName="10GbE Network Card", Description="Dual-port 10 Gigabit NIC.",
                Type=ComponentType.NIC, Tier=ComponentTier.Gen2,
                Price=200, BaseLifetimeHours=22f, DegradationPerHour=0.22f,
                NetworkSpeedGbps=10, PowerWatts=10f },
            new ComponentDef {
                Id="nic_g3", DisplayName="25GbE Network Card", Description="High-throughput 25 Gigabit NIC.",
                Type=ComponentType.NIC, Tier=ComponentTier.Gen3,
                Price=500, BaseLifetimeHours=30f, DegradationPerHour=0.15f,
                NetworkSpeedGbps=25, PowerWatts=15f },

            // ── Storage ───────────────────────────────────────────────────────
            new ComponentDef {
                Id="hdd_g1", DisplayName="1TB HDD",   Description="Spinning disk, reliable bulk storage.",
                Type=ComponentType.StorageDrive, Tier=ComponentTier.Gen1,
                Price=60,  BaseLifetimeHours=12f, DegradationPerHour=0.55f,
                StorageCapacityTb=1,  IsNvme=false, PowerWatts=8f },
            new ComponentDef {
                Id="ssd_g2", DisplayName="2TB SSD",   Description="Solid-state drive, fast and quiet.",
                Type=ComponentType.StorageDrive, Tier=ComponentTier.Gen2,
                Price=150, BaseLifetimeHours=20f, DegradationPerHour=0.3f,
                StorageCapacityTb=2,  IsNvme=false, PowerWatts=5f },
            new ComponentDef {
                Id="nvme_g3", DisplayName="4TB NVMe", Description="Ultra-fast NVMe SSD.",
                Type=ComponentType.StorageDrive, Tier=ComponentTier.Gen3,
                Price=350, BaseLifetimeHours=28f, DegradationPerHour=0.2f,
                StorageCapacityTb=4,  IsNvme=true,  PowerWatts=7f },

            // ── Cooling ───────────────────────────────────────────────────────
            new ComponentDef {
                Id="cool_g1", DisplayName="Basic Air Cooling",  Description="Standard chassis fan set.",
                Type=ComponentType.Cooling, Tier=ComponentTier.Gen1,
                Price=80,  BaseLifetimeHours=12f, DegradationPerHour=0.5f,
                CoolingEfficiency=1.0f, PowerWatts=20f },
            new ComponentDef {
                Id="cool_g2", DisplayName="2U Active Cooling",  Description="High-flow active cooling.",
                Type=ComponentType.Cooling, Tier=ComponentTier.Gen2,
                Price=200, BaseLifetimeHours=20f, DegradationPerHour=0.3f,
                CoolingEfficiency=0.75f, PowerWatts=30f },   // lower = better (multiplier)
            new ComponentDef {
                Id="cool_g3", DisplayName="Liquid Cooling Kit", Description="Closed-loop liquid cooling.",
                Type=ComponentType.Cooling, Tier=ComponentTier.Gen3,
                Price=600, BaseLifetimeHours=32f, DegradationPerHour=0.15f,
                CoolingEfficiency=0.5f, PowerWatts=40f },
        };

        // ── UPS definitions (stored separately — placed items, not rack components) ──
        public static readonly List<UpsDef> UpsUnits = new List<UpsDef>
        {
            new UpsDef { Id="ups_750",  DisplayName="750VA UPS",      Description="Compact UPS for 1-2 servers.",    Price=300,  CapacityVa=750,  MaxProtectedServers=2,  RuntimeMinutes=15 },
            new UpsDef { Id="ups_1500", DisplayName="1500VA UPS",     Description="Mid-range UPS for small racks.", Price=600,  CapacityVa=1500, MaxProtectedServers=4,  RuntimeMinutes=20 },
            new UpsDef { Id="ups_3000", DisplayName="3000VA Rack UPS",Description="High-capacity rack-mount UPS.",  Price=1200, CapacityVa=3000, MaxProtectedServers=10, RuntimeMinutes=30 },
        };

        // ── Chassis definitions ───────────────────────────────────────────────
        public static readonly List<ChassisDef> Chassis = new List<ChassisDef>
        {
            new ChassisDef { Id="chassis_1u",    DisplayName="1U Budget Chassis",      Price=300,
                             SizeInU=1, CpuSlots=1, RamSlots=2, PsuSlots=1, StorageSlots=2, Description="Entry-level 1U chassis." },
            new ChassisDef { Id="chassis_2u",    DisplayName="2U Standard Chassis",    Price=600,
                             SizeInU=2, CpuSlots=2, RamSlots=4, PsuSlots=1, StorageSlots=4, Description="Workhorse 2U server." },
            new ChassisDef { Id="chassis_2u_hd", DisplayName="2U High-Density Chassis",Price=1200,
                             SizeInU=2, CpuSlots=2, RamSlots=8, PsuSlots=2, StorageSlots=8, Description="Dense storage or compute node." },
            new ChassisDef { Id="chassis_blade", DisplayName="Blade Server Module",    Price=800,
                             SizeInU=1, CpuSlots=1, RamSlots=2, PsuSlots=0, StorageSlots=2, Description="Blade form factor. Shares PSU with blade enclosure." },
        };

        // ── Lookup helpers ────────────────────────────────────────────────────
        public static ComponentDef FindComponent(string id)
            => Components.Find(c => c.Id == id);

        public static ChassisDef FindChassis(string id)
            => Chassis.Find(c => c.Id == id);

        public static UpsDef FindUps(string id)
            => UpsUnits.Find(u => u.Id == id);
    }

    public class UpsDef
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public int    Price;
        public int    CapacityVa;
        public int    MaxProtectedServers;
        public int    RuntimeMinutes;  // battery runtime at full load
    }

    public class ChassisDef
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public int    Price;
        public int    SizeInU;
        public int    CpuSlots;
        public int    RamSlots;
        public int    PsuSlots;
        public int    StorageSlots;
    }
}
