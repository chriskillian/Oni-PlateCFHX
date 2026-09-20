using Xunit;

namespace PlateCounterflowHeatExchanger.Tests
{
    // How to read these tests.
    //
    // xUnit finds every public method marked [Fact] (one fixed case) or [Theory] (one case
    // per [InlineData] row) and runs it. A test passes if it returns; it fails if an Assert
    // throws. Names follow Subject_Condition_Expectation so a failure reads as a sentence.
    //
    // Each test checks one property of the physics rather than a table of numbers, so a
    // deliberate change to the formula breaks the tests that encode the property it
    // violated and no others. Floating point is compared with a tolerance unless the
    // arithmetic is exact by construction.
    //
    // Units match the model: J/K for heat capacity, K for temperature, W/K for
    // conductance, s for time.

    public class EffectivenessTests
    {
        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.5f)]
        [InlineData(1.0f)]
        public void Effectiveness_ZeroNtu_IsZero(float cr)
        {
            // Both branches give exactly 0 at NTU = 0: 0/(1+0) and (1-1)/(1-cr). Exact
            // comparison is safe here.
            Assert.Equal(0f, CounterflowThermalModel.Effectiveness(0f, cr));
        }

        [Theory]
        [InlineData(0.0f)]
        [InlineData(0.5f)]
        [InlineData(0.9f)]
        public void Effectiveness_LargeNtu_ApproachesOne_WhenUnbalanced(float cr)
        {
            // For Cr < 1 the exponential dies out and ε -> 1, but at a rate set by
            // e^{-NTU(1-Cr)}. "Large" therefore depends on Cr: at Cr = 0.9 an NTU of 50 gives
            // only ε = 0.9993 (first draft of this test failed there). NTU = 500 puts the
            // exponent at -50 or below for every row.
            float eps = CounterflowThermalModel.Effectiveness(500f, cr);
            Assert.InRange(eps, 1f - 1e-6f, 1f);
        }

        [Fact]
        public void Effectiveness_LargeNtu_ApproachesOneSlowly_WhenBalanced()
        {
            // Balanced counterflow approaches 1 only as NTU/(1+NTU): at NTU = 9, ε = 0.9.
            Assert.Equal(0.9, CounterflowThermalModel.Effectiveness(9f, 1f), 1e-6);
            Assert.InRange(CounterflowThermalModel.Effectiveness(1e6f, 1f), 1f - 1e-5f, 1f);
        }

        [Fact]
        public void Effectiveness_HandCalculation_Cr05_Ntu1()
        {
            // ε = (1 - e^{-NTU(1-Cr)}) / (1 - Cr e^{-NTU(1-Cr)})
            //   = (1 - e^{-0.5}) / (1 - 0.5 e^{-0.5}) = 0.393469 / 0.696735 = 0.564734
            Assert.Equal(0.56473, CounterflowThermalModel.Effectiveness(1f, 0.5f), 1e-4);
        }

        [Fact]
        public void Effectiveness_IsContinuous_AcrossBalancedThreshold()
        {
            // The balanced branch replaces a 0/0 limit. Just below the threshold the general
            // formula must agree with the balanced form to within float cancellation error.
            // Here at NTU = 2 the exact limit is 2/3.
            float below = CounterflowThermalModel.Effectiveness(2f, CounterflowThermalModel.BalancedThreshold - 1e-4f);
            float above = CounterflowThermalModel.Effectiveness(2f, CounterflowThermalModel.BalancedThreshold + 1e-4f);
            Assert.Equal(below, above, 1e-3);
            Assert.Equal(2.0 / 3.0, above, 1e-6);
        }

        [Fact]
        public void Effectiveness_NeverExceedsOne()
        {
            // Second law: no exchanger beats the ideal. Probe a grid of NTU and Cr.
            for (float ntu = 0f; ntu <= 100f; ntu += 2.5f)
            {
                for (float cr = 0f; cr <= 1f; cr += 0.05f)
                {
                    Assert.InRange(CounterflowThermalModel.Effectiveness(ntu, cr), 0f, 1f);
                }
            }
        }
    }

    public class ExchangeTests
    {
        // A representative unbalanced tick: stream A hot and small, stream B cold and large.
        private const float CA = 1500f, TA = 350f;
        private const float CB = 2500f, TB = 300f;
        private const float G = 800f, Dt = 1f;

        [Fact]
        public void Exchange_ConservesEnergy()
        {
            // What A loses, B gains: cA·ΔTA + cB·ΔTB = 0. The tolerance comes from float
            // resolution, not physics: a float near 330 K has a spacing (ulp) of about
            // 3e-5 K, and multiplying by 2500 J/K turns that into ~0.1 J per term.
            ExchangeResult r = CounterflowThermalModel.Exchange(CA, TA, CB, TB, G, Dt);
            double balance = CA * (r.TA - TA) + CB * (r.TB - TB);
            Assert.Equal(0.0, balance, 1.0);
        }

        [Fact]
        public void Exchange_HotStreamCools_ColdStreamWarms()
        {
            ExchangeResult r = CounterflowThermalModel.Exchange(CA, TA, CB, TB, G, Dt);
            Assert.True(r.Exchanged);
            Assert.True(r.TA < TA, "hot outlet must be below hot inlet");
            Assert.True(r.TB > TB, "cold outlet must be above cold inlet");
        }

        [Fact]
        public void Exchange_OutletsStayBetweenInlets()
        {
            // With ε <= 1 the hot outlet cannot fall below the cold inlet, nor the cold
            // outlet rise above the hot inlet. A temperature cross (hot outlet below cold
            // outlet) is allowed in counterflow and is not tested against here.
            ExchangeResult r = CounterflowThermalModel.Exchange(CA, TA, CB, TB, G, Dt);
            Assert.InRange(r.TA, TB, TA);
            Assert.InRange(r.TB, TB, TA);
        }

        [Fact]
        public void Exchange_HeatEqualsEffectivenessTimesCMinTimesDeltaT()
        {
            ExchangeResult r = CounterflowThermalModel.Exchange(CA, TA, CB, TB, G, Dt);
            Assert.Equal(CA, r.CMin);
            Assert.Equal(CA / CB, r.CapacityRatio, 1e-6);
            Assert.Equal(G * Dt / CA, r.Ntu, 1e-6);
            // Same expression as the model, so this is exact up to one rounding.
            Assert.Equal(r.Effectiveness * r.CMin * (TA - TB), r.Heat, 1e-2);
            // Recovering the heat from the outlet temperature goes through a float near
            // 331 K (ulp ~3e-5 K) times 1500 J/K: expect ~0.05 J of noise, allow 0.5 J.
            // The first draft used 0.01 J and failed by 0.017 J.
            Assert.Equal(r.Heat, CA * (TA - r.TA), 0.5);
        }

        [Fact]
        public void Exchange_IsSymmetricUnderSwappingStreams()
        {
            // Which stream is called A must not matter to the physics.
            ExchangeResult ab = CounterflowThermalModel.Exchange(CA, TA, CB, TB, G, Dt);
            ExchangeResult ba = CounterflowThermalModel.Exchange(CB, TB, CA, TA, G, Dt);
            Assert.Equal(ab.TA, ba.TB, 1e-4);
            Assert.Equal(ab.TB, ba.TA, 1e-4);
            Assert.Equal(ab.Heat, ba.Heat, 1e-2);
        }

        [Fact]
        public void Exchange_EqualInlets_MovesNothing()
        {
            ExchangeResult r = CounterflowThermalModel.Exchange(CA, 320f, CB, 320f, G, Dt);
            Assert.True(r.Exchanged);
            Assert.Equal(0f, r.Heat);
            Assert.Equal(320f, r.TA);
            Assert.Equal(320f, r.TB);
        }

        [Theory]
        [InlineData(0f, CA, CB)]     // no conductance (fully fouled or no plates)
        [InlineData(G, 0f, CB)]      // stream A carries no heat capacity
        [InlineData(G, CA, 0f)]      // stream B carries no heat capacity
        [InlineData(-5f, CA, CB)]    // nonsense conductance is treated as none
        public void Exchange_WithoutConductanceOrCapacity_LeavesTemperaturesAlone(float g, float cA, float cB)
        {
            ExchangeResult r = CounterflowThermalModel.Exchange(cA, TA, cB, TB, g, Dt);
            Assert.False(r.Exchanged);
            Assert.Equal(TA, r.TA);
            Assert.Equal(TB, r.TB);
        }

        [Fact]
        public void Exchange_HugeConductance_BalancedStreams_SwapsTemperatures()
        {
            // The ideal balanced counterflow exchanger swaps the two inlet temperatures.
            // With ε = NTU/(1+NTU) this is approached, never reached; at NTU = 1e6 the
            // shortfall is a few millikelvin on a 50 K span.
            ExchangeResult r = CounterflowThermalModel.Exchange(1000f, 350f, 1000f, 300f, 1e9f, 1f);
            Assert.Equal(300f, r.TA, 0.01);
            Assert.Equal(350f, r.TB, 0.01);
        }
    }

    // Snapshot of a result verified against the running game, not a physics truth. It
    // hard-codes constants that live outside the model (game specific heats, copper's
    // conductivity, the mod's footprint and packing factor), so a deliberate
    // recalibration will break it. That is the point: a change in these numbers must be
    // a decision, not an accident. Update the expected values and the date together.
    public class CharacterizationTests
    {
        [Fact]
        public void Exchange_MatchesInGameRig_EthanolVsWater_Copper_2026_09_18()
        {
            // Rig: full flow, copper plates, ceramic shell; 10 kg/s ethanol at 271.3 K
            // against 10 kg/s water at 275.0 K. Game log 2026-09-18 (ModDev/TESTING.md):
            // Gclean=81000 NTU=3.293 Cr=0.589 eps=0.875 A 271.3K->274.5K B 275.0K->273.1K.
            const float EthanolShc = 2.46f, WaterShc = 4.179f;   // game values, J/g/K
            const float CopperK = 60f, FootprintCells = 9f, PackingFactor = 150f;
            float cA = CounterflowThermalModel.HeatCapacity(10f, EthanolShc); // 24600 J/K
            float cB = CounterflowThermalModel.HeatCapacity(10f, WaterShc);   // 41790 J/K
            float g = CopperK * FootprintCells * PackingFactor;               // 81000 W/K

            ExchangeResult r = CounterflowThermalModel.Exchange(cA, 271.3f, cB, 275.0f, g, 1f);

            // The log prints three decimals for the dimensionless numbers and one for
            // temperatures; tolerances are half a unit in the last printed place.
            Assert.Equal(3.293, r.Ntu, 5e-4);
            Assert.Equal(0.589, r.CapacityRatio, 5e-4);
            Assert.Equal(0.875, r.Effectiveness, 5e-4);
            Assert.Equal(274.5, r.TA, 0.05);
            Assert.Equal(273.1, r.TB, 0.05);
        }
    }

    // The same invariants across the capacity magnitudes other exchanger types would
    // bring: liquid pipes carry 10 kg packets, gas pipes 1 kg, conveyor rails 20 kg solid
    // chunks. Cr spans 0.02 to 0.84 and NTU spans 2 to 80 here, well beyond the liquid
    // rig, so the float behaviour of the formula is exercised before any such extension.
    public class MediumRangeTests
    {
        public static readonly object[][] Cases =
        {
            //           cA (J/K)  tA     cB (J/K)  tB     G (W/K)   description
            new object[] { 24600f, 350f,  41790f,   300f,  81000f }, // ethanol vs water (liquid pipes)
            new object[] { 1005f,  400f,  846f,     300f,  81000f }, // 1 kg oxygen vs 1 kg CO2 (gas pipes)
            new object[] { 41790f, 350f,  1005f,    300f,  81000f }, // water vs oxygen (liquid vs gas)
            new object[] { 20000f, 600f,  41790f,   300f,  81000f }, // 20 kg rock chunk vs water (rail vs pipe)
            new object[] { 1f,     500f,  1e6f,     300f,  81000f }, // extreme Cr 1e-6, NTU 8.1e4
            new object[] { 24600f, 350f,  24600f,   300f,  1e9f   }, // balanced, huge NTU
        };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Exchange_ConservesEnergy_AcrossMedia(float cA, float tA, float cB, float tB, float g)
        {
            ExchangeResult r = CounterflowThermalModel.Exchange(cA, tA, cB, tB, g, 1f);
            Assert.True(r.Exchanged);
            double balance = cA * (r.TA - tA) + cB * (r.TB - tB);
            // Float temperatures near 300-600 K have an ulp of 3e-5 to 6e-5 K; scale the
            // joule tolerance with the capacities so large streams are not over-constrained.
            Assert.Equal(0.0, balance, 1e-4 * (cA + cB));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Exchange_RespectsSecondLaw_AcrossMedia(float cA, float tA, float cB, float tB, float g)
        {
            ExchangeResult r = CounterflowThermalModel.Exchange(cA, tA, cB, tB, g, 1f);
            float tHot = System.Math.Max(tA, tB), tCold = System.Math.Min(tA, tB);
            Assert.InRange(r.Effectiveness, 0f, 1f);
            Assert.InRange(r.Heat, 0f, r.CMin * (tHot - tCold) * (1f + 1e-6f));
            Assert.InRange(r.TA, tCold, tHot);
            Assert.InRange(r.TB, tCold, tHot);
        }
    }

    public class ShellLossTests
    {
        [Fact]
        public void ShellLoss_HotPacket_GivesHeatToBodyAndCools()
        {
            float q = CounterflowThermalModel.ShellLoss(2000f, 400f, 300f, 10f, 1f, out float tNew);
            Assert.True(q > 0f, "heat should flow into the body");
            Assert.True(tNew < 400f, "packet should cool");
            Assert.True(tNew > 300f, "packet should not pass the body temperature");
        }

        [Fact]
        public void ShellLoss_ColdPacket_DrawsHeatFromBodyAndWarms()
        {
            float q = CounterflowThermalModel.ShellLoss(2000f, 250f, 300f, 10f, 1f, out float tNew);
            Assert.True(q < 0f, "heat should flow out of the body");
            Assert.True(tNew > 250f, "packet should warm");
            Assert.True(tNew < 300f, "packet should not pass the body temperature");
        }

        [Fact]
        public void ShellLoss_EnergyLeavingPacket_EqualsEnergyReported()
        {
            const float c = 2000f, t = 400f;
            float q = CounterflowThermalModel.ShellLoss(c, t, 300f, 10f, 1f, out float tNew);
            // tNew near 400 K has ulp ~3e-5 K; times 2000 J/K gives ~0.06 J of noise.
            Assert.Equal(q, c * (t - tNew), 0.5);
            // Unclamped case: q = g·dt·ΔT = 10 · 1 · 100.
            Assert.Equal(1000f, q, 1e-3);
        }

        [Fact]
        public void ShellLoss_LargeConductance_ClampsAtBodyTemperature()
        {
            // g·dt = 1e6 W/K·s against c = 2000 J/K would overshoot by 500x; the clamp
            // stops the packet exactly at the body temperature.
            float q = CounterflowThermalModel.ShellLoss(2000f, 400f, 300f, 1e6f, 1f, out float tNew);
            Assert.Equal(300f, tNew, 1e-3);
            Assert.Equal(2000f * 100f, q, 1e-2);
        }

        [Fact]
        public void ShellLoss_AtBodyTemperature_DoesNothing()
        {
            float q = CounterflowThermalModel.ShellLoss(2000f, 300f, 300f, 10f, 1f, out float tNew);
            Assert.Equal(0f, q);
            Assert.Equal(300f, tNew);
        }

        [Fact]
        public void ShellLoss_ZeroCapacity_DoesNothing()
        {
            float q = CounterflowThermalModel.ShellLoss(0f, 400f, 300f, 10f, 1f, out float tNew);
            Assert.Equal(0f, q);
            Assert.Equal(400f, tNew);
        }
    }

    public class WallTemperatureTests
    {
        [Fact]
        public void WallTemperature_BothFlowing_IsMeanOfInlets()
        {
            Assert.Equal(325f, CounterflowThermalModel.WallTemperature(true, 350f, true, 300f));
        }

        [Fact]
        public void WallTemperature_OneFlowing_IsThatInlet()
        {
            Assert.Equal(350f, CounterflowThermalModel.WallTemperature(true, 350f, false, 300f));
            Assert.Equal(300f, CounterflowThermalModel.WallTemperature(false, 350f, true, 300f));
        }

        [Fact]
        public void HotInletTemperature_BothFlowing_IsHotterInlet()
        {
            Assert.Equal(350f, CounterflowThermalModel.HotInletTemperature(true, 350f, true, 300f));
            Assert.Equal(350f, CounterflowThermalModel.HotInletTemperature(true, 300f, true, 350f));
        }

        [Fact]
        public void HotInletTemperature_OneFlowing_IsThatInlet()
        {
            Assert.Equal(350f, CounterflowThermalModel.HotInletTemperature(true, 350f, false, 300f));
            Assert.Equal(300f, CounterflowThermalModel.HotInletTemperature(false, 350f, true, 300f));
        }
    }
}
