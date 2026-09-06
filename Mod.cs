using HarmonyLib;
using KMod;

namespace PlateCounterflowHeatExchanger
{
    public sealed class Mod : UserMod2
    {
        public override void OnLoad(Harmony harmony)
        {
            base.OnLoad(harmony); // applies any [HarmonyPatch] classes in this assembly
            UnityEngine.Debug.Log("[PlateCounterflowHX] Mod loaded.");
            AddStrings();
        }

        // The game looks up building text by these dotted keys. The middle segment
        // is the building id in upper case.
        private static void AddStrings()
        {
            string prefix = "STRINGS.BUILDINGS.PREFABS." +
                            PlateCounterflowHeatExchangerConfig.ID.ToUpperInvariant() + ".";
            Strings.Add(prefix + "NAME", "Plate Counterflow Heat Exchanger");
            Strings.Add(prefix + "DESC",
                "A passive plate heat exchanger. It moves heat between two fluid streams and draws no power.");
            Strings.Add(prefix + "EFFECT",
                "Transfers heat between two fluid streams. Fouls over time and needs periodic cleaning.");

            // Status items: the StatusItem constructor resolves these from its id (upper
            // case) and the "BUILDING" prefix. {Placeholders} are filled by the callbacks
            // in PCHXStatusItems.
            const string status = "STRINGS.BUILDING.STATUSITEMS.";
            Strings.Add(status + "PCHX_FOULING.NAME", "Fouling: {Fouling}");
            Strings.Add(status + "PCHX_FOULING.TOOLTIP",
                "Deposits on the plates add thermal resistance. Heat transfer is down {Fouling} from clean.\n\n" +
                "Deposits build up with flow but are also scoured away by it, and scouring grows faster than deposition. " +
                "Fouling therefore levels off instead of climbing forever, and it levels off LOWER at high flow. " +
                "Throttling a stream raises effectiveness but lets more deposit settle.\n\n" +
                "Hot plates speed scaling (Brine, Salt Water) and coking (Crude Oil, Petroleum). " +
                "Plates above 72 °C stop biological growth from Polluted Water. Water and Ethanol do not foul.\n\n" +
                "A Duplicant is sent to clean at {Threshold}. Cleaning stops both streams and drops the deposits as debris.\n\n" +
                "{Deposits}");
            Strings.Add(status + "PCHX_CLEANINGORDERED.NAME", "Cleaning ordered");
            Strings.Add(status + "PCHX_CLEANINGORDERED.TOOLTIP",
                "A Duplicant will open the plate pack and remove the deposits. Both streams stop while the plates are open.");
        }
    }

    // User-menu text. Plain constants for now: the menu takes strings, not string keys.
    // Localization (a LocString tree registered for translation) is a README to-do.
    public static class PCHXStrings
    {
        public const string CleanButton = "Clean Plates";
        public const string CleanButtonTooltip = "Order a Duplicant to open the plate pack and remove deposits. Both streams stop during cleaning.";
        public const string CancelCleanButton = "Cancel Cleaning";
        public const string CancelCleanButtonTooltip = "Withdraw the cleaning order.";
    }

    // Db.Initialize runs after buildings are generated and the plan screen exists,
    // so it is a safe point to drop our building into a build-menu category.
    [HarmonyPatch(typeof(Db), "Initialize")]
    public static class Db_Initialize_Patch
    {
        // Research node (Improved Plumbing) and build-menu home (Utilities / Temperature,
        // after the Aquatuner). Why these: README, "Build menu, research, and recipe".
        private const string UnlockTechId = "ImprovedLiquidPiping";

        // Subcategory taken from the game's enum rather than spelled out, so a rename by
        // Klei is a compile error here instead of a silent fall back to "uncategorized".
        private const string PlanCategory = "Utilities";
        private const string PlaceAfterBuilding = "LiquidConditioner";
        private static readonly string PlanSubcategory =
            TUNING.BUILDINGS.PlanSubcategoryName.temperature.ToString();

        public static void Prefix()
        {
            string id = PlateCounterflowHeatExchangerConfig.ID;

            // ModUtil takes the subcategory directly; the table is what the game consults
            // for its own buildings. Keep both in agreement.
            TUNING.BUILDINGS.PLANSUBCATEGORYSORTING[id] = PlanSubcategory;

            ModUtil.AddBuildingToPlanScreen(
                (HashedString)PlanCategory, id, PlanSubcategory, PlaceAfterBuilding);
        }

        // The tech tree only exists once Db.Initialize has run, so this is a Postfix. TryGet
        // rather than Get: a wrong id degrades to an ungated building plus a log line.
        public static void Postfix()
        {
            // Our status items, created once the game's own Db exists.
            PCHXStatusItems.Create();

            Tech tech = Db.Get().Techs.TryGet(UnlockTechId);
            if (tech == null)
            {
                UnityEngine.Debug.LogWarning(
                    $"[PlateCounterflowHX] tech '{UnlockTechId}' not found; building left ungated.");
                return;
            }
            tech.unlockedItemIDs.Add(PlateCounterflowHeatExchangerConfig.ID);
        }
    }
}
