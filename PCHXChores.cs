using System.Collections.Generic;

namespace PlateCounterflowHeatExchanger
{
    // Our own chore type, so the errand reads "Clean Plates" rather than "Empty Storage".
    //
    // Database.ChoreTypes.Add is private, but it only wraps ChoreType's public constructor,
    // which registers itself with its parent set, resolves its chore groups, and creates the
    // duplicant status item. Priorities are copied from EmptyStorage instead of taking the
    // next implicit slot: the counter falls by 50 per vanilla type, so a type appended after
    // the constructor would rank below every vanilla chore, Idle included, and duplicants
    // would idle rather than clean.
    public static class PCHXChores
    {
        public const string CleanPlatesId = "PCHX_CleanPlates";

        // Null if creation failed; the workable then falls back to EmptyStorage.
        public static ChoreType CleanPlates;

        public static void Create()
        {
            Database.ChoreTypes types = Db.Get().ChoreTypes;
            ChoreType model = types.EmptyStorage;

            // Same chore groups as the model (Basekeeping and Hauling today), read from the
            // model rather than spelled out so a Klei change follows through.
            string[] groups = new string[model.groups.Length];
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = model.groups[i].Id;
            }

            CleanPlates = new ChoreType(
                CleanPlatesId, types, groups,
                "",                       // no urge, like EmptyStorage
                STRINGS.DUPLICANTS.CHORES.PCHX_CLEANPLATES.NAME,
                STRINGS.DUPLICANTS.CHORES.PCHX_CLEANPLATES.STATUS,
                STRINGS.DUPLICANTS.CHORES.PCHX_CLEANPLATES.TOOLTIP,
                new List<Tag>(),          // no interrupt exclusions, like EmptyStorage
                model.priority, model.explicitPriority);

            // interruptPriority is not a constructor argument. Database.ChoreTypes assigns it
            // to every built-in type in a table at the end of its constructor (Idle 96400,
            // EmptyStorage 96700), which has already run by the time we get here, so ours
            // would stay 0. ChoreConsumer.ChooseChore only lets a candidate displace the
            // current chore if its interruptPriority is strictly greater, and Idle is a
            // chore; at 0 the errand could never interrupt an idle duplicant and would be
            // picked up only in the gaps between chores. Copy the model's.
            CleanPlates.interruptPriority = model.interruptPriority;
        }
    }
}
