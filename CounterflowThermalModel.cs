using System;

namespace PlateCounterflowHeatExchanger
{
    // Result of one tick of counterflow exchange between two streams. Plain numbers only.
    public struct ExchangeResult
    {
        public bool Exchanged;        // false when nothing could move (no conductance or an empty stream)
        public float TA;              // outlet temperature of stream A, K
        public float TB;              // outlet temperature of stream B, K
        public float Effectiveness;   // ε, dimensionless, 0..1
        public float Heat;            // energy moved hot -> cold this tick, J, >= 0
        public float Ntu;             // number of transfer units, G·dt / Cmin
        public float CapacityRatio;   // Cr = Cmin / Cmax
        public float CMin;            // smaller heat capacity this tick, J/K
    }

    // The thermal physics of the exchanger, with no game types. Every input is a plain
    // number in SI units: heat capacities in J/K (a packet's mass times its specific heat,
    // for one tick), temperatures in K, conductances in W/K, time in s. The game-facing
    // code in HeatExchangerCore looks up elements, converts units and applies the results
    // to packets; this class only does arithmetic. That split is what lets
    // tests/ThermalModel.Tests compile this file on its own and run it without the game.
    //
    // Numerics use System.Math rather than UnityEngine.Mathf so the file compiles under
    // both net48 (the mod) and net10.0 (the tests). MathF is absent from .NET Framework,
    // so double-precision Math with casts is the portable choice. Mathf.Exp is itself
    // (float)Math.Exp, so the in-game numbers are unchanged by the move.
    public static class CounterflowThermalModel
    {
        // Above this capacity ratio the general ε formula divides 0 by 0; the balanced
        // form takes over.
        public const float BalancedThreshold = 0.999f;

        // Heat capacity of one packet in J/K. Mass is in kg but the game's specific heat is
        // per gram, hence the factor 1000.
        public static float HeatCapacity(float massKg, float specificHeatPerGram)
        {
            return massKg * 1000f * specificHeatPerGram;
        }

        // Counterflow effectiveness ε(NTU, Cr). The balanced case (Cr = 1) is a removable
        // singularity in the general formula, so handle it on its own.
        public static float Effectiveness(float ntu, float cr)
        {
            float eps;
            if (cr > BalancedThreshold)
            {
                eps = ntu / (1f + ntu);
            }
            else
            {
                float ex = (float)Math.Exp(-ntu * (1f - cr));
                eps = (1f - ex) / (1f - cr * ex);
            }
            return Clamp01(eps);
        }

        // One tick of exchange between stream A (capacity cA J/K at tA K) and stream B
        // through a wall of conductance G W/K over dt seconds. Returns the outlet
        // temperatures and the intermediate quantities the readout and the debug log use.
        public static ExchangeResult Exchange(float cA, float tA, float cB, float tB, float conductance, float dt)
        {
            var r = new ExchangeResult { Exchanged = false, TA = tA, TB = tB };
            if (conductance <= 0f || cA <= 0f || cB <= 0f)
            {
                return r;
            }

            float cMin = Math.Min(cA, cB);
            float cMax = Math.Max(cA, cB);
            float cr = cMin / cMax;
            float ntu = conductance * dt / cMin;
            float eps = Effectiveness(ntu, cr);

            float tHot = Math.Max(tA, tB);
            float tCold = Math.Min(tA, tB);
            float q = eps * cMin * (tHot - tCold); // energy moved hot -> cold, >= 0

            // Apply equal-and-opposite energy. The hot stream loses what the cold gains.
            if (tA >= tB)
            {
                r.TA = tA - q / cA;
                r.TB = tB + q / cB;
            }
            else
            {
                r.TA = tA + q / cA;
                r.TB = tB - q / cB;
            }

            r.Exchanged = true;
            r.Effectiveness = eps;
            r.Heat = q;
            r.Ntu = ntu;
            r.CapacityRatio = cr;
            r.CMin = cMin;
            return r;
        }

        // Heat a packet of capacity c J/K at t K gives to a body at tBody K through
        // conductance g W/K over dt seconds. Positive values warm the body; negative values
        // draw from it. Clamped so the packet cannot overshoot the body temperature.
        // tNew is the packet's temperature afterwards.
        public static float ShellLoss(float c, float t, float tBody, float g, float dt, out float tNew)
        {
            tNew = t;
            if (c <= 0f)
            {
                return 0f;
            }
            float dT = t - tBody;
            float q = g * dt * dT;
            float qMax = c * dT; // would bring the packet exactly to tBody
            if (Math.Abs(q) > Math.Abs(qMax))
            {
                q = qMax;
            }
            tNew = t - q / c;
            return q;
        }

        // Estimated plate temperature: the mean of both inlets when both flow, or the single
        // flowing inlet otherwise. With neither flowing the answer is tB, which callers
        // never use (they check for flow first).
        public static float WallTemperature(bool aFlowing, float tA, bool bFlowing, float tB)
        {
            if (aFlowing && bFlowing) return 0.5f * (tA + tB);
            if (aFlowing) return tA;
            return tB;
        }

        private static float Clamp01(float v)
        {
            if (v < 0f) return 0f;
            if (v > 1f) return 1f;
            return v;
        }
    }
}
