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

    // Asymptotic (Kern-Seaton style) fouling: deposition grows with throughput, shear removal
    // with throughput squared, so the deposit levels off. State is MASS per byproduct
    // element; thermal resistance is derived from mass. Model, pacing history, and the
    // deliberate choices behind Apply: README.md, "Fouling model".
    public static class Fouling
    {
        // ---- Tuning knobs ----

        // Thermal resistance added per kilogram of deposit, K/W. A property of the deposit,
        // not the wall, so the same kilogram costs a high-k exchanger a larger share.
        public const float ResistancePerKg = 1e-5f;

        // Removal time constant at full flow, seconds. Deposition rates and this constant
        // were scaled together by 3 for pacing; equilibria are unchanged (README, "Pacing").
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
        // Base-game liquids only; DLC fluids are a README to-do. SimHashes carries DLC
        // members regardless of enabled DLCs, so unconditional entries compile.
        private static readonly Dictionary<SimHashes, FoulingSpec> Table = new Dictionary<SimHashes, FoulingSpec>
        {
            // Rates are kg deposit per kg fluid at f(T) = 1.
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
        // strips off returns to it as the flowing element. The packet can never grow past
        // its planned capacity, so the output cell still accepts it in full.
        //
        // Deliberate, not oversights (README, "Deliberate choices"): shear scours only the
        // flowing fluid's own byproduct; fluids with no table entry return early and so
        // never scour (a fouled exchanger cannot be flushed); returned mass joins the fluid.
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
