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

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                ID,
                3,                              // width
                3,                              // height
                "metalrefinery_kanim",          // borrowed art (drawn for 3x4; looks tall for now)
                100,                            // hit points
                60f,                            // construction time (seconds)
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER5,
                MATERIALS.ALL_METALS,           // built from any metal
                2400f,                          // melting point (K)
                BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.NONE,
                noise: NOISE_POLLUTION.NONE);

            // Stream A, the one vanilla-supported liquid stream for this increment.
            // x is centered: for a 3-wide building, valid x offsets are -1, 0, +1.
            // Input bottom-left (-1,0), output bottom-right (1,0). Stream B will use
            // the top row later: input (-1,2), output (1,2).
            def.InputConduitType = ConduitType.Liquid;
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(-1, 0);
            def.UtilityOutputOffset = new CellOffset(1, 0);

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
            // A small liquid buffer the consumer fills and the dispenser empties.
            Storage storage = BuildingTemplates.CreateDefaultStorage(go);
            storage.allowItemRemoval = false;
            storage.storageFilters = STORAGEFILTERS.LIQUIDS;
            storage.capacityKg = 100f;
            storage.SetDefaultStoredItemModifiers(GasReservoirConfig.ReservoirStoredItemModifiers);

            // Pulls liquid from the input pipe into the storage above.
            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.ignoreMinMassCheck = true;
            consumer.forceAlwaysSatisfied = true;
            consumer.alwaysConsume = true;
            consumer.capacityKG = storage.capacityKg;

            // Pushes whatever is in storage back out the output pipe (any element).
            ConduitDispenser dispenser = go.AddOrGet<ConduitDispenser>();
            dispenser.conduitType = ConduitType.Liquid;
            dispenser.elementFilter = null;
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            // Coordinates the storage/consumer/dispenser lifecycle for stream A.
            go.AddOrGetDef<StorageController.Def>();
            go.GetComponent<KPrefabID>().AddTag(GameTags.OverlayBehindConduits);

            // Stream B: a second liquid input+output driven manually, ports on the top row.
            SecondaryLiquidStream streamB = go.AddOrGet<SecondaryLiquidStream>();
            streamB.inputOffset = new CellOffset(1, 2);    // top-right
            streamB.outputOffset = new CellOffset(-1, 2);  // top-left
            // Thermal and fouling components arrive in later steps.
        }
    }
}
