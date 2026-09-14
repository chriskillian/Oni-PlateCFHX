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
            PatchLocalization(harmony);
        }

        // Register every LocString in our STRINGS tree under the game's own dotted keys
        // (STRINGS.BUILDINGS.PREFABS.<ID>.NAME and so on). English is in place from mod
        // load; the Localization patch below re-registers translated text later.
        private static void AddStrings()
        {
            LocString.CreateLocStringKeys(typeof(STRINGS), null);
        }

        // Localization.Initialize is where the game picks its language. Postfixing it is the
        // conventional place for mods to register their string tree for translation. Patched
        // by hand rather than with an attribute so that a missing or renamed method degrades
        // to a warning and English text, instead of failing the whole mod load.
        private static void PatchLocalization(Harmony harmony)
        {
            try
            {
                System.Reflection.MethodInfo target = AccessTools.Method(typeof(Localization), "Initialize");
                if (target == null)
                {
                    UnityEngine.Debug.LogWarning("[PlateCounterflowHX] Localization.Initialize not found; strings stay English.");
                    return;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(typeof(Mod), nameof(OnLocalizationInitialized)));
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning("[PlateCounterflowHX] could not patch Localization.Initialize; strings stay English. " + e);
            }
        }

        // 1. RegisterForTranslation: lists our assembly with the translation loader and keys
        //    the tree as PlateCounterflowHeatExchanger.STRINGS.*, the form .po files use.
        // 2. CreateLocStringKeys(root, null): re-registers the (possibly translated) text
        //    under the vanilla STRINGS.* keys the game and our StatusItems read.
        // Loading the mod's own translations/<locale>.po files would sit between the two.
        public static void OnLocalizationInitialized()
        {
            Localization.RegisterForTranslation(typeof(STRINGS));
            LocString.CreateLocStringKeys(typeof(STRINGS), null);
        }
    }

    // Db.Initialize runs after buildings are generated and the plan screen exists,
    // so it is a safe point to drop our building into a build-menu category.
    [HarmonyPatch(typeof(Db), "Initialize")]
    public static class Db_Initialize_Patch
    {
        // Research node (Liquid Tuning, the Aquatuner's tech, one tier below Improved
        // Plumbing) and build-menu home (Utilities, the Aquatuner's group, after the
        // Aquatuner). Tech ids are not their display names; see Database.Techs.
        private const string UnlockTechId = "LiquidTemperature";

        // Subcategory taken from the game's enum rather than spelled out, so a rename by
        // Klei is a compile error here instead of a silent fall back to "uncategorized".
        // Must equal the Aquatuner's own PLANSUBCATEGORYSORTING entry (`temperature`).
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
            // Our status items and chore type, created once the game's own Db exists.
            PCHXStatusItems.Create();
            try
            {
                PCHXChores.Create();
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning(
                    "[PlateCounterflowHX] could not create the Clean Plates chore type; " +
                    "falling back to Empty Storage. " + e);
            }

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
