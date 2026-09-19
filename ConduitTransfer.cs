using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // One tick's planned transfer on one stream (the fluid that WILL move from the input
    // cell to the output cell this tick). It is a plan, without storage. Nothing is held between
    // ticks, so there is no state to serialize, no invisible mass, and no double-counting
    // against ONI's own thermal sim. Mirrors ConduitBridge, which also holds nothing.
    public struct Packet
    {
        public SimHashes Element;
        public float Mass;        // what will be pushed to the output (after fouling adjusts it)
        public float SourceMass;  // what will be removed from the input (the planned amount)
        public float Capacity;    // free room in the output cell (Mass may never exceed this)
        public float Temperature;
        public byte DiseaseIdx;
        public int DiseaseCount;

        public bool IsEmpty => Mass <= 0f;
    }

    // Moves fluid between the exchanger's pipe cells, bridge-style, one stream at a time.
    // Plan predicts what ConduitFlow will accept this tick; the coordinator fouls and heats
    // the planned packet; Commit then performs the move. Home of the mass bookkeeping
    // between the input pipe, the output pipe and the fouling deposits.
    public static class ConduitTransfer
    {
        // Predict this tick's transfer on one stream by asking ConduitFlow's own acceptance
        // helpers, the same rules AddElement applies. Returns an empty Packet when nothing
        // will move. (Until 2026-09-18 this mirrored the rules by hand; review finding F4.)
        public static Packet Plan(ConduitFlow flow, int inCell, int outCell)
        {
            Packet p = default;
            if (!flow.HasConduit(inCell) || !flow.HasConduit(outCell))
            {
                return p;
            }

            ConduitFlow.ConduitContents src = flow.GetContents(inCell);
            if (src.mass <= 0f)
            {
                return p;
            }

            // CanMergeContents: same element, or an empty output cell, and some room.
            ConduitFlow.ConduitContents dst = flow.GetContents(outCell);
            if (!flow.CanMergeContents(src, dst, src.mass))
            {
                return p;
            }

            // GetAmountAllowedForMerging caps at the output cell's free space. Asking for an
            // unbounded amount returns that free space itself, which fouling needs as the
            // ceiling for returned (scoured) mass.
            float capacity = flow.GetAmountAllowedForMerging(src, dst, float.MaxValue);
            float mass = flow.GetAmountAllowedForMerging(src, dst, src.mass);
            if (mass <= 0f)
            {
                return p;
            }

            p.Element = src.element;
            p.Mass = mass;
            p.SourceMass = mass;
            p.Capacity = capacity;
            p.Temperature = src.temperature;
            p.DiseaseIdx = src.diseaseIdx;
            // Disease rides along in proportion to the mass moved (same as ConduitBridge).
            p.DiseaseCount = (int)(mass / src.mass * src.diseaseCount);
            return p;
        }

        // Move the planned packet. Add to the output at its (possibly changed) temperature
        // and mass, then remove the PLANNED mass from the input. The two differ by whatever
        // fouling deposited or returned, so mass is conserved across fluid + deposit.
        // Plan used the game's own acceptance rules, so accepted should equal p.Mass. If the
        // output takes less anyway, remove input mass in the same proportion, so a rule change
        // in the game shrinks the transfer instead of destroying fluid, and log it.
        // Returns the mass the output cell accepted (what actually moved this tick).
        public static float Commit(ConduitFlow flow, int inCell, int outCell, Packet p)
        {
            if (p.IsEmpty)
            {
                return 0f;
            }
            float accepted = flow.AddElement(
                outCell, p.Element, p.Mass, p.Temperature, p.DiseaseIdx, p.DiseaseCount);
            if (accepted <= 0f)
            {
                return 0f;
            }
            float fraction = accepted / p.Mass;
            if (fraction < 1f - 0.0001f)
            {
                Debug.LogWarning($"[PCHX] acceptance mismatch: planned {p.Mass:F3} kg, output took {accepted:F3} kg; removing {fraction:P1} of the planned input");
            }
            flow.RemoveElement(inCell, p.SourceMass * Mathf.Min(fraction, 1f));
            return accepted;
        }
    }
}
