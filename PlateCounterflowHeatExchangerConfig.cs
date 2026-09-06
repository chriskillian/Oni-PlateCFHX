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

        // The single source of truth for all four port cells. x is centered: for a 3-wide
        // building valid x offsets are -1, 0, +1; y is bottom-origin (0..height-1). Stream A
        // (primary) runs along the bottom row left->right; stream B (secondary) runs along
        // the top row right->left, so the two flow counter to each other.
        public static readonly CellOffset PrimaryInput = new CellOffset(-1, 0);   // bottom-left
        public static readonly CellOffset PrimaryOutput = new CellOffset(1, 0);   // bottom-right
        public static readonly CellOffset SecondaryInput = new CellOffset(1, 2);  // top-right
        public static readonly CellOffset SecondaryOutput = new CellOffset(-1, 2); // top-left

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                ID,
                3,                              // width
                3,                              // height
                "metalrefinery_kanim",          // borrowed art (drawn for 3x4; looks tall for now)
                100,                            // hit points
                60f,                            // construction time (seconds)
                // Parallel arrays: one mass per material. Refined metal for the plates plus
                // gaskets to seal the pack (the Steam Turbine uses the same pairing, with 4
                // gaskets for a 5x3 footprint; 2 fits our 3x3). Gaskets also research-gate
                // the building behind Improved Plumbing, since that is where they unlock.
                new float[] { BUILDINGS.CONSTRUCTION_MASS_KG.TIER5[0], 2f },
                new string[] { "RefinedMetal", "BuildingGasket" },
                2400f,                          // melting point (K)
                BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.NONE,
                noise: NOISE_POLLUTION.NONE);

            // Stream A's primary ports. Setting these on the def creates the port icons
            // and network endpoints for free; we drive the flow through them by hand in
            // HeatExchangerCore, so no ConduitConsumer/Dispenser is attached.
            def.InputConduitType = ConduitType.Liquid;
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = PrimaryInput;
            def.UtilityOutputOffset = PrimaryOutput;

            def.Floodable = false;
            def.Overheatable = false;           // a heat exchanger is meant to run hot
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.LiquidConduits.ID;
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs, ID);
            // Deliberately no EnergyConsumer / RequiresPowerInput: the device is passive.
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            // No Storage/ConduitConsumer/ConduitDispenser: both streams are moved cell-to-cell
            // by HeatExchangerCore in the same stateless way ConduitBridge does, so nothing is
            // ever held inside the building. See that class for the reasoning.
        }

        // The secondary ports (stream B) declare themselves through ISecondaryInput/
        // ISecondaryOutput. On the finished building HeatExchangerCore implements those;
        // during placement and construction it does not exist yet, so attach lightweight
        // marker components there so the port icons still show. (This mirrors GasFilter.)
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

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayBehindConduits);

            // Drives both streams and (from 3b) exchanges heat between them.
            HeatExchangerCore core = go.AddOrGet<HeatExchangerCore>();
            core.secondaryInputOffset = SecondaryInput;
            core.secondaryOutputOffset = SecondaryOutput;
            // Fouling/cleaning components arrive in later steps.
        }
    }
}
