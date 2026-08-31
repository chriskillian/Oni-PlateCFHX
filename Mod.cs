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
        }
    }

    // Db.Initialize runs after buildings are generated and the plan screen exists,
    // so it is a safe point to drop our building into a build-menu category.
    [HarmonyPatch(typeof(Db), "Initialize")]
    public static class Db_Initialize_Patch
    {
        public static void Prefix()
        {
            // (category, buildingId, subcategory). "uncategorized" is the catch-all
            // subcategory; we can refine placement later.
            ModUtil.AddBuildingToPlanScreen(
                (HashedString)"Plumbing",
                PlateCounterflowHeatExchangerConfig.ID,
                "uncategorized");
        }
    }
}
