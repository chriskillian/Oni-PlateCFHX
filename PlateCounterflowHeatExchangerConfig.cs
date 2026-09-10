using TUNING;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // The game scans every loaded assembly for IBuildingConfig subclasses and
    // instantiates each one to build its BuildingDef. So this class existing is
    // enough to create the building; no manual registration of the def is needed.
    public class PlateCounterflowHeatExchangerConfig : IBuildingConfig
    {
        // One canonical id. Strings, the plan-menu entry, and the prefab all key off it.
        public const string ID = "PlateCounterflowHeatExchanger";

        // The single source of truth for all four port cells (README, "Geometry and ports").
        // x is centered (-1, 0, +1 for a 3-wide building); y is bottom-origin.
        public static readonly CellOffset PrimaryInput = new CellOffset(-1, 0);   // bottom-left
        public static readonly CellOffset PrimaryOutput = new CellOffset(1, 0);   // bottom-right
        public static readonly CellOffset SecondaryInput = new CellOffset(1, 2);  // top-right
        public static readonly CellOffset SecondaryOutput = new CellOffset(-1, 2); // top-left

        // Custom art. The game loads anim/assets/PCHX/ and registers it as "PCHX_kanim" (ART.md).
        public const string AnimName = "PCHX_kanim";
        private const string FallbackAnimName = "metalrefinery_kanim"; // borrowed art, drawn for 3x4

        public override BuildingDef CreateBuildingDef()
        {
            // Fall back to borrowed art if the kanim folder failed to load, so a bad art
            // build costs a wrong-looking building rather than a null-def crash at startup.
            string anim = Assets.GetAnim(AnimName) != null ? AnimName : FallbackAnimName;
            if (anim != AnimName)
            {
                Debug.LogWarning("[PCHX] kanim " + AnimName + " not loaded; using " + FallbackAnimName);
            }

            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                ID,
                3,                              // width
                3,                              // height
                anim,
                100,                            // hit points
                60f,                            // construction time (seconds)
                // Parallel arrays, one mass per material tag (borrowed from SteamTurbineConfig2).
                // See README, "Build menu, research, and recipe" for material input reasoning.
                // Slot 0 (refined metal) is the PrimaryElement: it drives effectiveness,
                // melting, and the body the sim conducts to the room. Slot 2 is the shell
                // insulation; its conductivity sets room heat loss (THERMAL.md, "Shell heat").
                new float[] { BUILDINGS.CONSTRUCTION_MASS_KG.TIER5[0], 2f, BUILDINGS.CONSTRUCTION_MASS_KG.TIER3[0] },
                new string[] { "RefinedMetal", "BuildingGasket", "Insulator" },
                2400f,                          // melting point (K)
                BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.NONE,
                noise: NOISE_POLLUTION.NONE);

            // Stream A's primary ports. Setting these on the def creates the port icons
            // and network endpoints for free. Flow is driven by hand in
            // HeatExchangerCore, so no ConduitConsumer/Dispenser is attached.
            def.InputConduitType = ConduitType.Liquid;
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = PrimaryInput;
            def.UtilityOutputOffset = PrimaryOutput;

            def.Floodable = false;
            def.Overheatable = false;           // a heat exchanger is meant to run hot
            // def.ThermalConductivity stays at the default: the measured body-to-room leg
            // is two orders above any non-Insulite shell, so the insulation, not this
            // value, limits room loss (THERMAL.md, "Shell heat", Shell calibration).
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.LiquidConduits.ID;
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs, ID);
            // Passive heat exchanger, no EnergyConsumer / RequiresPowerInput is deliberate.
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            // No Storage/ConduitConsumer/ConduitDispenser: both streams are moved cell-to-cell
            // by HeatExchangerCore in the same stateless way ConduitBridge does, so nothing is
            // ever held inside the building. See that class for the reasoning.
        }

        // The secondary ports (stream B) declare themselves through ISecondaryInput/
        // ISecondaryOutput. Implemented on the finished building by HeatExchangerCore.
        // Doesnt exist during placement and construction, so attach lightweight
        // marker components there to make the port icons show (see GasFilter).
        private void AttachSecondaryPorts(GameObject go)
        {
            go.AddComponent<ConduitSecondaryInput>().portInfo =
                new ConduitPortInfo(ConduitType.Liquid, SecondaryInput);
            go.AddComponent<ConduitSecondaryOutput>().portInfo =
                new ConduitPortInfo(ConduitType.Liquid, SecondaryOutput);
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
        {
            base.DoPostConfigurePreview(def, go);
            AttachSecondaryPorts(go);
        }

        public override void DoPostConfigureUnderConstruction(GameObject go)
        {
            base.DoPostConfigureUnderConstruction(go);
            AttachSecondaryPorts(go);
        }

        // def.InputConduitType / OutputConduitType make the game attach a ConduitConsumer and
        // RequireInputs / RequireOutputs to the completed-building prefab. We drive the cells by
        // hand and warn per port ourselves, so none of them may live on the building. Remove
        // them from the PREFAB, before any instance exists. Removing them per instance at spawn
        // (the previous approach) was too late: Destroy is deferred to end of frame, so
        // RequireInputs still ran its own OnSpawn and raised the vanilla "No Liquid Intake" /
        // "Liquid Pipe Empty" items, then died with nothing left to clear them.
        // DestroyImmediate on a prefab has no such race. Verified 2026-09-08: consumer,
        // RequireInputs and RequireOutputs are all present here; no dispenser is ever attached.
        private static void StripVanillaPlumbing(GameObject go)
        {
            StripComponent<ConduitConsumer>(go);
            StripComponent<ConduitDispenser>(go);
            StripComponent<RequireInputs>(go);
            StripComponent<RequireOutputs>(go);
        }

        private static void StripComponent<T>(GameObject go) where T : Component
        {
            T c = go.GetComponent<T>();
            Debug.Log("[PCHX] prefab " + typeof(T).Name + ": " + (c != null ? "removed" : "not present"));
            if (c != null)
            {
                UnityEngine.Object.DestroyImmediate(c, true);
            }
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayBehindConduits);
            StripVanillaPlumbing(go);

            // Drives both streams, exchanges heat between them, and keeps the fouling ledgers.
            HeatExchangerCore core = go.AddOrGet<HeatExchangerCore>();
            core.secondaryInputOffset = SecondaryInput;
            core.secondaryOutputOffset = SecondaryOutput;

            // Cleaning errand: user-menu button, automatic trigger, and the duplicant
            // work that empties the ledgers into debris.
            go.AddOrGet<FoulingCleanWorkable>();
        }
    }
}
