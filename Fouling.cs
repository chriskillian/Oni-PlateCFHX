using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // Which wall temperature a mechanism responds to. Most use the mean of the two inlets.
    // Coking uses the hotter flowing inlet: in a counterflow exchanger the hot-end film
    // temperature sets the coking rate, and the hot inlet fixes it, not the mean
    // (DEPOSIT_TUNING.md, decision 1C).
    public enum WallReference
    {
        Mean,
        HotInlet,
    }

    // How one fluid fouls the plates. DepositionRate is kg of deposit laid down per kg of
    // fluid moved, at a temperature factor of 1 (R_max in DEPOSIT_TUNING.md). TempFactor
    // scales that by the wall temperature (kelvin) selected by Wall. Byproduct is what the
    // deposit is made of, and what a duplicant removes when cleaning.
    public struct FoulingSpec
    {
        public float DepositionRate;
        public Func<float, float> TempFactor;
        public WallReference Wall;
        public SimHashes Byproduct;

        public FoulingSpec(float rate, Func<float, float> tempFactor, WallReference wall, SimHashes byproduct)
        {
            DepositionRate = rate;
            TempFactor = tempFactor;
            Wall = wall;
            Byproduct = byproduct;
        }
    }

    // Asymptotic (Kern-Seaton style) fouling. Deposition grows with throughput, shear removal
    // with throughput squared, so the deposit levels off. State lives in a game Storage per
    // stream side, holding the byproducts as real solid chunks with temperature; thermal
    // resistance is derived from the stored mass. See FOULING.md.
    public static class Fouling
    {
        // ---- Tuning knobs ----

        // Thermal resistance added per kilogram of deposit, K/W. A property of the deposit,
        // not the wall, so the same kilogram costs a high-k exchanger a larger share.
        public const float ResistancePerKg = 1e-5f;

        // Removal time constant at full flow, seconds. Together with the deposition rates it
        // sets how fast fouling approaches equilibrium. The equilibrium itself depends only
        // on the product of the two (FOULING.md, "Pacing"). At full flow the approach time
        // constant is this value itself, so a multi-cycle cleaning interval needs it near
        // the interval (DEPOSIT_TUNING.md, decision 2A).
        public const float RemovalTimeConstant = 2700f;

        // Flow that counts as "full" for the shear term. One full liquid packet per tick.
        public const float ReferenceMassPerTick = ConduitFlow.MAX_LIQUID_MASS;

        // Temperature factors. Wall temperature in kelvin. Every factor is bounded in [0, 1];
        // DepositionRate alone sets the ceiling on what a packet can lose.

        // Biological film, grows below pasteurization (~72 C / 345 K), dies above it.
        private static float Biological(float t) => t < 345f ? 1f : 0f;

        // Scaling by inverse-solubility salts, none at room temperature, rising linearly to
        // full strength at 100 C and flat above: once the salt is out of solution a hotter
        // wall adds nothing (DEPOSIT_TUNING.md, decision 6A).
        private static float Scaling(float t) => Mathf.Clamp01((t - 293f) / 80f);

        // Coking, a logistic in the wall temperature: an Arrhenius rise doubling every 25 K
        // below the knee at 600 K (the real coking onset, 550 to 650 K film temperature),
        // saturating at 1 above it as transport rather than reaction becomes the limit.
        // Written with the growing term in the denominator on purpose. The equal form
        // 2^x / (1 + 2^x) overflows Pow to infinity on a hot wall and yields NaN; this form
        // drives Pow toward zero there and only grows on a cold wall, where it would need a
        // wall 3200 K below the knee to overflow (DEPOSIT_TUNING.md, decision 1C).
        private static float Coking(float t) => 1f / (1f + Mathf.Pow(2f, -(t - 600f) / 25f));

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
            // Rates are kg deposit per kg fluid at f(T) = 1 (R_max). Calibrated so the common row
            // of each family deposits 8e-5 of each packet at its anchor wall, which gives a
            // cleaning errand about every 4.5 cycles at full flow on steel (DEPOSIT_TUNING.md,
            // decision 2A); the other rows keep their earlier ratios to that row. Anchors:
            // brine f = 0.51 at a 334 K mean wall; crude f = 0.90 at a 680 K hot inlet; the
            // bounded families at f = 1. A rate may never exceed the fluid's yaml ore fraction
            // (mass conservation, ENGINE.md); these sit two orders below it.
            // Where the yaml names a solid the liquid leaves behind on boiling
            // (highTempTransitionOreId), that is the byproduct. The oil chain ends in sulfur
            // (sour gas condensing), so crude and naphtha deposit sulfur, not carbon.
            { SimHashes.DirtyWater,   new FoulingSpec(8e-5f,   Biological,  WallReference.Mean,     SimHashes.Dirt) },
            { SimHashes.Mucus,        new FoulingSpec(1.6e-4f, Biological,  WallReference.Mean,     SimHashes.SlimeMold) },

            { SimHashes.SaltWater,    new FoulingSpec(4.6e-5f, Scaling,     WallReference.Mean,     SimHashes.Salt) },
            { SimHashes.Brine,        new FoulingSpec(1.6e-4f, Scaling,     WallReference.Mean,     SimHashes.Salt) },
            { SimHashes.MurkyBrine,   new FoulingSpec(1.6e-4f, Scaling,     WallReference.Mean,     SimHashes.Salt) },
            { SimHashes.SugarWater,   new FoulingSpec(1.5e-4f, Scaling,     WallReference.Mean,     SimHashes.Sucrose) },

            { SimHashes.CrudeOil,     new FoulingSpec(8.9e-5f, Coking,      WallReference.HotInlet, SimHashes.Sulfur) },
            { SimHashes.Petroleum,    new FoulingSpec(1.8e-5f, Coking,      WallReference.HotInlet, SimHashes.Sulfur) },
            { SimHashes.Naphtha,      new FoulingSpec(1.8e-5f, Coking,      WallReference.HotInlet, SimHashes.Sulfur) },
            { SimHashes.LiquidGunk,   new FoulingSpec(8.9e-5f, Coking,      WallReference.HotInlet, SimHashes.Sulfur) },
            { SimHashes.PhytoOil,     new FoulingSpec(4.4e-5f, Coking,      WallReference.HotInlet, SimHashes.Algae) },
            { SimHashes.RefinedLipid, new FoulingSpec(1.8e-5f, Coking,      WallReference.HotInlet, SimHashes.RefinedCarbon) },
            { SimHashes.Resin,        new FoulingSpec(8.9e-5f, Coking,      WallReference.HotInlet, SimHashes.Isoresin) },
            { SimHashes.NaturalResin, new FoulingSpec(8.9e-5f, Coking,      WallReference.HotInlet, SimHashes.RefinedCarbon) },
            { SimHashes.Latex,        new FoulingSpec(5.9e-5f, Coking,      WallReference.HotInlet, SimHashes.Rubber) },

            { SimHashes.Ink,          new FoulingSpec(8e-5f,   Particulate, WallReference.Mean,     SimHashes.RefinedCarbon) },

            { SimHashes.Milk,         new FoulingSpec(8e-5f,   Waxing,      WallReference.Mean,     SimHashes.MilkFat) },
            { SimHashes.FishMilk,     new FoulingSpec(8e-5f,   Waxing,      WallReference.Mean,     SimHashes.MilkFat) },
        };

        public static bool TryGetSpec(SimHashes fluid, out FoulingSpec spec) => Table.TryGetValue(fluid, out spec);

        // Total fouling resistance of one stream's deposits, K/W.
        public static float ResistanceOf(Storage deposits) => deposits.MassStored() * ResistancePerKg;

        // Deposit mass at which conductance is 1% of clean, used as the storage capacity
        // (DEPOSIT_TUNING.md, decision 5A). D50 = 1 / (G_clean * ResistancePerKg) is the mass
        // that halves conductance; 99 * D50 leaves 1%. Steel: ~136 kg; thermium: ~33 kg.
        public static float CapacityFor(float cleanConductance)
        {
            if (cleanConductance <= 0f) return 0f;
            return 99f / (cleanConductance * ResistancePerKg);
        }

        // Apply one tick of fouling for a moving packet. Adjusts the deposits and the packet's
        // mass so that mass is conserved. Whatever is deposited leaves the fluid, and whatever
        // shear strips off returns to it as the flowing element. The packet can never grow past
        // its planned capacity, so the output cell still accepts it in full; when returned mass
        // would overfill it, the packet is capped and SourceMass is lowered instead, so the
        // difference stays in the input pipe for the next tick (bridge-style backing up).
        //
        // By design, shear scours only the flowing fluid's own byproduct. Fluids with no table
        // entry return early and never scour (a fouled exchanger cannot be flushed).
        // Returned mass joins the fluid. See FOULING.md, "Deliberate choices".
        public static void Apply(ref Packet p, Storage deposits, float meanWall, float hotInlet, float dt)
        {
            if (p.IsEmpty || deposits == null || !TryGetSpec(p.Element, out FoulingSpec spec))
            {
                return;
            }

            // Deposition. Proportional to mass moved and to the temperature factor, taken at
            // the wall this mechanism responds to (mean of the inlets, or the hotter inlet).
            // Capped at the storage's remaining capacity (DEPOSIT_TUNING.md, decision 5A): a
            // plugged exchanger stops depositing and the excess stays in the packet. The
            // capacity is set where conductance is already ~1% of clean, so this is an
            // accounting bound, not something a player reaches at the calibrated rates.
            float wall = spec.Wall == WallReference.HotInlet ? hotInlet : meanWall;
            float deposition = spec.DepositionRate * spec.TempFactor(wall) * p.Mass;
            deposition = Mathf.Clamp(deposition, 0f, Mathf.Max(0f, deposits.RemainingCapacity()));

            // Shear removal. Proportional to the existing deposit of THIS byproduct and to
            // flow squared, relative to a full packet per tick.
            float existing = deposits.GetMassAvailable(spec.Byproduct);
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
                if (returned <= 0f) return;

                // Returned mass comes back at the chunk's temperature and mixes into the packet
                // mass-weighted (F6). Read the temperature before consuming: the chunk may be
                // deleted when its mass reaches zero. In the rare capped case the share left in
                // the input pipe keeps the pipe's temperature; that slip is accepted.
                PrimaryElement chunk = deposits.FindPrimaryElement(spec.Byproduct);
                float chunkTemperature = chunk != null ? chunk.Temperature : p.Temperature;
                p.Temperature = (p.Mass * p.Temperature + returned * chunkTemperature) / (p.Mass + returned);

                float excess = Mathf.Max(0f, p.Mass + returned - p.Capacity);
                p.SourceMass = Mathf.Max(0f, p.SourceMass - excess);
                p.Mass = Mathf.Min(p.Mass + returned, p.Capacity);
                deposits.ConsumeIgnoringDisease(spec.Byproduct.CreateTag(), returned);
            }
            else
            {
                // Deposit. Never take more than the packet holds. The deposit enters the storage
                // at the packet's temperature (decision 2026-09-19): mass and its heat leave the
                // fluid together, the game's own convention on a state change, so deposition
                // neither creates nor destroys energy (F6). AddOre merges into the existing chunk
                // of this byproduct and mixes temperature mass-weighted; disease is not carried.
                net = Mathf.Min(net, p.Mass);
                if (net <= 0f) return;
                p.Mass -= net;
                deposits.AddOre(spec.Byproduct, net, p.Temperature, byte.MaxValue, 0);
            }
        }
    }
}
