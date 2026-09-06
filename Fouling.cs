using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // How one fluid fouls the plates. DepositionRate is kg of deposit laid down per kg of
    // fluid moved, at a temperature factor of 1. TempFactor scales that by the wall
    // temperature (kelvin). Byproduct is what the deposit is made of, and what a duplicant
    // shovels out when cleaning.
    public struct FoulingSpec
    {
        public float DepositionRate;
        public Func<float, float> TempFactor;
        public SimHashes Byproduct;

        public FoulingSpec(float rate, Func<float, float> tempFactor, SimHashes byproduct)
        {
            DepositionRate = rate;
            TempFactor = tempFactor;
            Byproduct = byproduct;
        }
    }

    // The fouling model: an asymptotic (Kern-Seaton style) balance between deposition,
    // which grows with throughput, and shear removal, which grows with throughput squared.
    // Because removal outpaces deposition as flow rises, the deposit levels off, and levels
    // off LOWER at high flow. Running the exchanger full open keeps it cleaner; throttling
    // for higher effectiveness costs fouling.
    //
    // Deposits are tracked as MASS per byproduct element (the saved state). Thermal
    // resistance is derived from mass, so mass is the single source of truth and cleaning
    // can hand the exact deposit back as solid chunks.
    public static class Fouling
    {
        // ---- Tuning knobs (see the design notes; start values, refine from the log) ----

        // Thermal resistance added per kilogram of deposit, K/W. Copper's clean resistance
        // is about 1.2e-5 K/W, so ~1.2 kg of deposit halves a copper exchanger's conductance.
        // The same kilogram costs a thermium exchanger a larger share: fouling resistance
        // is a property of the deposit, not the wall, exactly as in real exchangers.
        public const float ResistancePerKg = 1e-5f;

        // Removal time constant at full flow, seconds. At full flow the deposit relaxes
        // toward its asymptote with this time constant.
        //
        // Pacing note (2026-09-06): asymptotic deposit = deposition rate x time constant, so
        // scaling every DepositionRate UP and this constant DOWN by the same factor speeds
        // the whole system up without moving any equilibrium. First test ran at 1800 s with
        // rates a third of the current ones: physically sane but a throttled brine loop took
        // ~41 cycles to reach 50% fouling. Factor 3 applied: one cycle here, throttled brine
        // now reaches 50% in ~19 cycles, full-flow brine still settles near 27% at a 322 K wall.
        public const float RemovalTimeConstant = 600f;

        // Flow that counts as "full" for the shear term: one full liquid packet per tick.
        public const float ReferenceMassPerTick = ConduitFlow.MAX_LIQUID_MASS;

        // Temperature factors. Wall temperature in kelvin.

        // Biological film: grows below pasteurization (~72 C / 345 K), dies above it.
        private static float Biological(float t) => t < 345f ? 1f : 0f;

        // Scaling by inverse-solubility salts: none at room temperature, rising linearly to
        // full strength at 100 C, and continuing to climb above (brine boils near 103 C).
        private static float Scaling(float t) => Mathf.Max(0f, (t - 293f) / 80f);

        // Coking: a mild Arrhenius stand-in, doubling every 25 K, equal to 1 at 100 C.
        private static float Coking(float t) => Mathf.Pow(2f, (t - 373f) / 25f);

        // Fluids not listed here do not foul (Water, Ethanol, and anything unexpected).
        //
        // TODO (DLC fluids): the table covers base-game liquids only. DLC liquids need
        // entries and byproducts: Mucin (Aquatic Planet Pack; SimHashes.Mucus / SolidMucus)
        // will certainly foul; Naphtha, Resin, Nectar, and Phyto Oil are candidates.
        // Confirmed 2026-09-06: SimHashes carries DLC (and even unused, e.g. SolidPropane)
        // members regardless of enabled DLCs, so unconditional entries compile and are inert
        // when the element never flows. Runtime check still owed: ElementLoader lookup of a
        // disabled-DLC hash must not throw in Fouling.Apply's callers.
        private static readonly Dictionary<SimHashes, FoulingSpec> Table = new Dictionary<SimHashes, FoulingSpec>
        {
            // Rates are kg deposit per kg fluid at f(T) = 1 (x3 pacing applied, see above).
            { SimHashes.DirtyWater, new FoulingSpec(1.5e-4f, Biological, SimHashes.Dirt) },
            { SimHashes.SaltWater,  new FoulingSpec(6e-5f,   Scaling,    SimHashes.Salt) },
            { SimHashes.Brine,      new FoulingSpec(2.1e-4f, Scaling,    SimHashes.Salt) },
            { SimHashes.CrudeOil,   new FoulingSpec(3e-4f,   Coking,     SimHashes.RefinedCarbon) },
            { SimHashes.Petroleum,  new FoulingSpec(6e-5f,   Coking,     SimHashes.Sulfur) },
        };

        public static bool TryGetSpec(SimHashes fluid, out FoulingSpec spec) => Table.TryGetValue(fluid, out spec);

        // Total fouling resistance of one stream's ledger, K/W.
        public static float ResistanceOf(Dictionary<SimHashes, float> ledger)
        {
            float total = 0f;
            foreach (KeyValuePair<SimHashes, float> kv in ledger)
            {
                total += kv.Value;
            }
            return total * ResistancePerKg;
        }

        public static float TotalMass(Dictionary<SimHashes, float> ledger)
        {
            float total = 0f;
            foreach (KeyValuePair<SimHashes, float> kv in ledger)
            {
                total += kv.Value;
            }
            return total;
        }

        // Apply one tick of fouling for a moving packet. Adjusts the ledger and the packet's
        // mass so that mass is conserved: what deposits leaves the fluid, and what shear
        // strips off returns to it (salt redissolving into brine, dirt into polluted water).
        // The packet can never grow past its planned capacity, so the output cell still
        // accepts it in full.
        public static void Apply(ref Packet p, Dictionary<SimHashes, float> ledger, float wallTemperature, float dt)
        {
            if (p.IsEmpty || !TryGetSpec(p.Element, out FoulingSpec spec))
            {
                return;
            }

            // Deposition: proportional to mass moved and to the temperature factor.
            float deposition = spec.DepositionRate * spec.TempFactor(wallTemperature) * p.Mass;

            // Shear removal: proportional to the existing deposit of THIS byproduct and to
            // flow squared, relative to a full packet per tick.
            ledger.TryGetValue(spec.Byproduct, out float existing);
            float flowFraction = p.Mass / ReferenceMassPerTick;
            float removal = flowFraction * flowFraction * existing * dt / RemovalTimeConstant;

            float net = deposition - removal;

            // Never strip more than is there, never take more than the packet holds, and
            // never return more than the output cell has room for.
            if (net < 0f)
            {
                net = Mathf.Max(net, -existing);
                net = Mathf.Max(net, p.Mass - p.Capacity);
            }
            else
            {
                net = Mathf.Min(net, p.Mass);
            }

            ledger[spec.Byproduct] = existing + net;
            p.Mass -= net;
        }
    }
}
