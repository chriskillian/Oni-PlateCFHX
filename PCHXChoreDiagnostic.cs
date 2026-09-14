using System.Collections.Generic;
using HarmonyLib;

namespace PlateCounterflowHeatExchanger
{
    // TEMPORARY diagnostic for the idle-duplicant issue (see TESTING.md, "Idle-duplicant
    // watch item"). Delete this file once diagnosed.
    //
    // The creation-time log in FoulingCleanWorkable.OrderClean showed every value we
    // control is correct (basic/5 on chore and building, 5650/5000 on the chore type),
    // yet the errand still loses to the current chore at evaluation time. This patch
    // observes the losing comparison itself: after ChoreConsumer.FindNextChore has run
    // its preconditions, it finds the context for our Clean Plates chore in the
    // duplicant's precondition snapshot and logs which precondition failed, the values
    // the comparison saw on our side, and the same values for the chore the duplicant
    // is currently holding. IsMoreSatisfyingEarly/Late compare, in order: master
    // priority class, personal priority, master priority value, then context.priority
    // against the current chore type's priority.
    [HarmonyPatch(typeof(ChoreConsumer), "FindNextChore")]
    public static class ChoreConsumer_FindNextChore_Diagnostic
    {
        // static readonly rather than const so the early return is not "unreachable code".
        private static readonly bool Enabled = true;
        // One line per duplicant per this many seconds of wall time.
        private const float MinIntervalSeconds = 2f;
        private static readonly Dictionary<int, float> lastLogByConsumer = new Dictionary<int, float>();

        public static void Postfix(ChoreConsumer __instance, bool __result)
        {
            if (!Enabled) return;
            try
            {
                Log(__instance, __result);
            }
            catch (System.Exception e)
            {
                // A diagnostic must never take the game down with it.
                UnityEngine.Debug.LogWarning("[PCHX] chore diagnostic threw: " + e);
            }
        }

        private static void Log(ChoreConsumer consumer, bool foundChore)
        {
            // The snapshot's accessor and list names are not known for certain (a direct
            // GetPreconditionSnapshot() call did not compile), so locate them by type and
            // name fragment through reflection.
            List<Chore.Precondition.Context> succeededList, failedList;
            if (!GetSnapshotLists(consumer, out succeededList, out failedList))
            {
                WarnOnce("could not find the precondition snapshot lists on ChoreConsumer");
                return;
            }

            // Only duplicants for whom one of our errands is a candidate at all.
            Chore.Precondition.Context unused = default(Chore.Precondition.Context);
            if (!Find(succeededList, ref unused) && !Find(failedList, ref unused)) return;

            int id = consumer.GetInstanceID();
            float now = UnityEngine.Time.realtimeSinceStartup;
            float last;
            if (lastLogByConsumer.TryGetValue(id, out last) && now - last < MinIntervalSeconds) return;
            lastLogByConsumer[id] = now;

            // Run of 2026-09-14 settled the priority fields (class, personal, value, priority
            // all as designed; the errand passes its preconditions and is still not chosen),
            // so this version shows what FindNextChore had to choose from instead.
            ChoreDriver driver = consumer.GetComponent<ChoreDriver>();
            Chore current = driver == null ? null : driver.GetCurrentChore();
            string line = "[PCHX] eval dupe=" + consumer.GetProperName()
                + " cell=" + Grid.PosToCell(consumer)
                + " found=" + foundChore
                + " current=" + (current == null ? "none" : Describe(current))
                // Our type was built after Database.ChoreTypes finished, so whatever its tail
                // assigns (interruptPriority showed as 0 for us) we never received. Print the
                // model's value beside ours.
                + " interrupt(ours/EmptyStorage/current)=" + (PCHXChores.CleanPlates == null ? -1 : PCHXChores.CleanPlates.interruptPriority)
                + "/" + Db.Get().ChoreTypes.EmptyStorage.interruptPriority
                + "/" + (current == null ? -1 : current.choreType.interruptPriority);

            // Every passing candidate, in the snapshot's order (FindNextChore sorts the list
            // ascending by CompareTo, so if the order is preserved the best is last).
            line += " | passed[" + (succeededList == null ? 0 : succeededList.Count) + "]:";
            if (succeededList != null)
            {
                for (int i = 0; i < succeededList.Count; i++)
                {
                    Chore.Precondition.Context c = succeededList[i];
                    line += " " + Describe(c.chore) + " cost=" + c.cost + " ok=" + c.IsSuccess()
                        + (IsOurs(c.chore) ? " skipEarly=" + c.skipMoreSatisfyingEarlyPrecondition : "");
                }
            }

            // Our errands that failed, with the precondition that stopped them.
            line += " | ourFailed:";
            if (failedList != null)
            {
                for (int i = 0; i < failedList.Count; i++)
                {
                    Chore.Precondition.Context c = failedList[i];
                    if (!IsOurs(c.chore)) continue;
                    List<Chore.PreconditionInstance> preconditions = c.chore.GetPreconditions();
                    string failed = (c.failedPreconditionId >= 0 && c.failedPreconditionId < preconditions.Count)
                        ? DescribePrecondition(preconditions[c.failedPreconditionId])
                        : "index " + c.failedPreconditionId;
                    line += " " + Describe(c.chore) + " failed=" + failed + " cost=" + c.cost;
                }
            }

            UnityEngine.Debug.Log(line);
        }

        // "PCHX_CleanPlates#123@cell(basic/5,driver=Ada)": type, chore id, target cell,
        // master priority, and who (if anyone) is already driving it. The driver field is
        // read by name through Traverse so a wrong guess degrades to "?" not a build break.
        private static string Describe(Chore chore)
        {
            if (chore == null) return "null";
            string driverName = "none";
            object driver = Traverse.Create(chore).Field("driver").GetValue();
            if (driver is ChoreDriver) driverName = ((ChoreDriver)driver).GetProperName();
            else if (driver == null && !Traverse.Create(chore).Field("driver").FieldExists()) driverName = "?";
            string cell = chore.target == null ? "?" : Grid.PosToCell(chore.target.gameObject).ToString();
            return chore.choreType.Id + "#" + chore.id + "@" + cell
                + "(" + chore.masterPriority.priority_class + "/" + chore.masterPriority.priority_value
                + ",driver=" + driverName + ")";
        }

        private static bool IsOurs(Chore chore)
        {
            return chore != null && chore.target != null
                && chore.target.GetComponent<FoulingCleanWorkable>() != null;
        }

        // ---- Reflection helpers: Klei's member names here were guessed wrong once. ----

        private static bool warned;
        private static void WarnOnce(string message)
        {
            if (warned) return;
            warned = true;
            UnityEngine.Debug.LogWarning("[PCHX] chore diagnostic: " + message);
        }

        // Finds the ChoreConsumer field holding the precondition snapshot (matched by its
        // type name containing "Snapshot"), then within it the two List<Context> fields,
        // told apart by "succe"/"fail" in their names. If only one list name matches, the
        // other list is treated as empty rather than giving up.
        private static bool GetSnapshotLists(ChoreConsumer consumer,
            out List<Chore.Precondition.Context> succeeded, out List<Chore.Precondition.Context> failed)
        {
            succeeded = null;
            failed = null;
            object snapshot = null;
            // FindNextChore (decompiled 2026-09-14) keeps two of these: preconditionSnapshot,
            // filled every call, and lastSuccessfulPreconditionSnapshot, a copy taken only
            // when a chore was chosen. We want the live one, so skip any field named "last".
            foreach (System.Reflection.FieldInfo f in AccessTools.GetDeclaredFields(typeof(ChoreConsumer)))
            {
                if (f.IsStatic) continue;
                if (f.FieldType.Name.IndexOf("Snapshot", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (f.Name.IndexOf("last", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                snapshot = f.GetValue(consumer);
                if (snapshot != null) break;
            }
            if (snapshot == null) return false;

            foreach (System.Reflection.FieldInfo f in AccessTools.GetDeclaredFields(snapshot.GetType()))
            {
                if (f.FieldType != typeof(List<Chore.Precondition.Context>)) continue;
                string name = f.Name.ToLowerInvariant();
                if (name.Contains("succe")) succeeded = (List<Chore.Precondition.Context>)f.GetValue(snapshot);
                else if (name.Contains("fail")) failed = (List<Chore.Precondition.Context>)f.GetValue(snapshot);
            }
            return succeeded != null || failed != null;
        }

        // PreconditionInstance has no `id` field. Report every string-valued field it does
        // have (id, description, or whatever Klei named them) joined with "/".
        private static string DescribePrecondition(Chore.PreconditionInstance instance)
        {
            object boxed = instance;
            string result = "";
            foreach (System.Reflection.FieldInfo f in AccessTools.GetDeclaredFields(boxed.GetType()))
            {
                object v = f.GetValue(boxed);
                if (v is string)
                    result += (result.Length == 0 ? "" : "/") + (string)v;
                else if (v != null && f.FieldType.Name == "Precondition")
                    result += (result.Length == 0 ? "" : "/") + Traverse.Create(v).Field("id").GetValue<string>();
            }
            return result.Length == 0 ? "unnamed" : result;
        }

        private static bool Find(List<Chore.Precondition.Context> contexts, ref Chore.Precondition.Context found)
        {
            if (contexts == null) return false;
            for (int i = 0; i < contexts.Count; i++)
            {
                if (IsOurs(contexts[i].chore)) { found = contexts[i]; return true; }
            }
            return false;
        }
    }
}
