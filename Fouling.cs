using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // How one fluid fouls the plates. DepositionRate is kg of deposit laid down per kg of
    // fluid moved, at a temperature factor of 1. TempFactor scales that by the wall
    // temperature (kelvin). Byproduct is what the deposit is made of, and what a duplicant
    // removes when cleaning.
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

    // Asymptotic (Kern-Seaton style) fouling. Deposition grows with throughput, shear removal
    // with throughput squared, so the deposit levels off. State is MASS per byproduct
    // element. Thermal resistance is derived from mass. See FOULING.md.
    public static class Fouling
    {
        // ---- Tuning knobs ----

        // Thermal resistance added per kilogram of deposit, K/W. A property of the deposit,
        // not the wall, so the same kilogram costs a high-k exchanger a larger share.
        public const float ResistancePerKg = 1e-5f;

        // Removal time constant at full flow, seconds. Together with the deposition rates it
        // sets how fast fouling approaches equilibrium. The equilibrium itself depends only
        // on the product of the two (FOULING.md, "Pacing").
        public const float RemovalTimeConstant = 600f;

        // Flow that counts as "full" for the shear term. One full liquid packet per tick.
        public const float ReferenceMassPerTick = ConduitFlow.MAX_LIQUID_MASS;

        // Temperature factors. Wall temperature in kelvin.

        // Biological film, grows below pasteurization (~72 C / 345 K), dies above it.
        private static float Biological(float t) => t < 345f ? 1f : 0f;

        // Scaling by inverse-solubility salts, none at room temperature, rising linearly to
        // full strength at 100 C, and continuing to climb above (brine boils near 103 C).
        private static float Scaling(float t) => Mathf.Max(0f, (t - 293f) / 80f);

        // Coking, a mild Arrhenius stand-in, doubling every 25 K, equal to 1 at 100 C.
        private static float Coking(float t) => Mathf.Pow(2f, (t - 373f) / 25f);

        // Particulate, suspended solids settle whatever the temperature.
        private static float Particulate(float t) => 1f;

        // Waxing, dissolved wax comes out on a cold wall, the mirror image of scaling. Full
        // strength at -10 C and below, none at 50 C and above (Brackene boils at 80 C).
        private static float Waxing(float t) => Mathf.Clamp01((323f - t) / 60f);

        // Every liquid in the game's elements/liquid.yaml is classified in FOULING.md, "Liquid
        // classification". Anything not listed below does not cause fouling.
        // SimHashes carries every yaml element regardless of enabled DLCs, so unconditional
        // entries compile and a liquid that never appears simply never matches.
        private static readonly Dictionary<SimHashes, FoulingSpec> Table = new Dictionary<SimHashes, FoulingSpec>
        {
            // Rates are kg deposit per kg fluid at f(T) = 1. Where the yaml names a solid the
            // liquid leaves behind on boiling (highTempTransitionOreId), that is the byproduct.
            { SimHashes.DirtyWater,   new FoulingSpec(1.5e-4f, Biological,  SimHashes.Dirt) },
            { SimHashes.Mucus,        new FoulingSpec(3e-4f,   Biological,  SimHashes.SlimeMold) },

            { SimHashes.SaltWater,    new FoulingSpec(6e-5f,   Scaling,     SimHashes.Salt) },
            { SimHashes.Brine,        new FoulingSpec(2.1e-4f, Scaling,     SimHashes.Salt) },
            { SimHashes.MurkyBrine,   new FoulingSpec(2.1e-4f, Scaling,     SimHashes.Salt) },
            { SimHashes.SugarWater,   new FoulingSpec(2e-4f,   Scaling,     SimHashes.Sucrose) },

            { SimHashes.CrudeOil,     new FoulingSpec(3e-4f,   Coking,      SimHashes.RefinedCarbon) },
            { SimHashes.Petroleum,    new FoulingSpec(6e-5f,   Coking,      SimHashes.Sulfur) },
            { SimHashes.Naphtha,      new FoulingSpec(6e-5f,   Coking,      SimHashes.RefinedCarbon) },
            { SimHashes.LiquidGunk,   new FoulingSpec(3e-4f,   Coking,      SimHashes.Sulfur) },
            { SimHashes.PhytoOil,     new FoulingSpec(1.5e-4f, Coking,      SimHashes.Algae) },
            { SimHashes.RefinedLipid, new FoulingSpec(6e-5f,   Coking,      SimHashes.RefinedCarbon) },
            { SimHashes.Resin,        new FoulingSpec(3e-4f,   Coking,      SimHashes.Isoresin) },
            { SimHashes.NaturalResin, new FoulingSpec(3e-4f,   Coking,      SimHashes.RefinedCarbon) },
            { SimHashes.Latex,        new FoulingSpec(2e-4f,   Coking,      SimHashes.Rubber) },

            { SimHashes.Ink,          new FoulingSpec(1e-4f,   Particulate, SimHashes.RefinedCarbon) },

            { SimHashes.Milk,         new FoulingSpec(1.5e-4f, Waxing,      SimHashes.MilkFat) },
            { SimHashes.FishMilk,     new FoulingSpec(1.5e-4f, Waxing,      SimHashes.MilkFat) },
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
        // mass so that mass is conserved. Whatever is deposited leaves the fluid, and whatever
        // shear strips off returns to it as the flowing element. The packet can never grow past
        // its planned capacity, so the output cell still accepts it in full; when returned mass
        // would overfill it, the packet is capped and SourceMass is lowered instead, so the
        // difference stays in the input pipe for the next tick (bridge-style backing up).
        //
        // By design, shear scours only the flowing fluid's own byproduct. Fluids with no table
        // entry return early and never scour (a fouled exchanger cannot be flushed).
        // Returned mass joins the fluid. See FOULING.md, "Deliberate choices".
        public static void Apply(ref Packet p, Dictionary<SimHashes, float> ledger, float wallTemperature, float dt)
        {
            if (p.IsEmpty || !TryGetSpec(p.Element, out FoulingSpec spec))
            {
                return;
            }

            // Deposition. Proportional to mass moved and to the temperature factor.
            float deposition = spec.DepositionRate * spec.TempFactor(wallTemperature) * p.Mass;

            // Shear removal. Proportional to the existing deposit of THIS byproduct and to
            // flow squared, relative to a full packet per tick.
            ledger.TryGetValue(spec.Byproduct, out float existing);
            float flowFraction = p.Mass / ReferenceMassPerTick;
            float removal = flowFraction * flowFraction * existing * dt / RemovalTimeConstant;

            float net = deposition - removal;

            if (net < 0f)
            {
                // Scour. Never strip more than is there, and never return more than the output
                // cell could hold in total. The returned mass joins the packet. If that overfills
                // the output cell, cap the packet at the room and lower SourceMass by the excess,
                // so the input pipe keeps the difference for the next tick. Input loss plus
                // deposit loss then equals the output gain exactly. (Until 2026-09-19 the scour
                // itself was clamped to the room, so a full packet into an empty output cell
                // never scoured and a deposit built at low flow never fell; review finding F2.)
                net = Mathf.Max(net, -existing);
                net = Mathf.Max(net, -p.Capacity);
                float returned = -net;
                float excess = Mathf.Max(0f, p.Mass + returned - p.Capacity);
                p.SourceMass = Mathf.Max(0f, p.SourceMass - excess);
                p.Mass = Mathf.Min(p.Mass + returned, p.Capacity);
            }
            else
            {
                // Deposit. Never take more than the packet holds.
                net = Mathf.Min(net, p.Mass);
                p.Mass -= net;
            }

            ledger[spec.Byproduct] = existing + net;
        }
    }
}
