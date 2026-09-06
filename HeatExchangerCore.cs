using System.Collections.Generic;
using KSerialization;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // One tick's planned transfer on one stream: the fluid that WILL move from the input
    // cell to the output cell this tick. It is a plan, not storage. Nothing is held between
    // ticks, so there is no state to serialize, no invisible mass, and no double-counting
    // against ONI's own thermal sim. Mirrors ConduitBridge, which also holds nothing.
    public struct Packet
    {
        public SimHashes Element;
        public float Mass;        // what will be pushed to the output (after fouling adjusts it)
        public float SourceMass;  // what will be removed from the input (the planned amount)
        public float Capacity;    // free room in the output cell; Mass may never exceed this
        public float Temperature;
        public byte DiseaseIdx;
        public int DiseaseCount;

        public bool IsEmpty => Mass <= 0f;
    }

    // The device core. Drives both liquid streams by hand and exchanges heat between them
    // (counterflow, ε-NTU, effectiveness set by the construction material's thermal
    // conductivity). The two streams never share mass; a counterflow exchanger trades heat,
    // not fluid.
    //
    // Flow follows the vanilla ConduitBridge pattern: read the input packet, add to the
    // output, then remove from the input only what the output accepted. We add one step the
    // bridge does not need. Because the heat math has to know the mass that really moves,
    // we predict the output's acceptance first (same rule ConduitFlow.AddElement applies:
    // element must match or the cell must be empty, capped by free space under MaxMass),
    // exchange heat between the two predicted packets, then commit both.
    //
    // Stream A rides the building's PRIMARY conduit ports (from the def's
    // Utility{Input,Output}Offset); we read/write those cells manually instead of via
    // ConduitConsumer/Dispenser/Storage. Stream B rides two SECONDARY ports this component
    // declares (ISecondaryInput/ISecondaryOutput → icons + placement validation) and
    // registers with the liquid network itself.
    //
    // Persistence: the fouling ledgers are the only state that must survive a save. Klei's
    // serializer is OPT-IN: the class attribute below says "save nothing unless marked", and
    // each [Serialize] field is what gets written. Everything else (cells, endpoints, the
    // cached clean conductance) is rebuilt in OnSpawn from the building itself.
    [SerializationConfig(MemberSerialization.OptIn)]
    public class HeatExchangerCore : KMonoBehaviour, ISecondaryInput, ISecondaryOutput
    {
        // Stream B (secondary) offsets, set by the config before spawn. Stream A's offsets
        // live on the BuildingDef, so they are not repeated here.
        public CellOffset secondaryInputOffset;
        public CellOffset secondaryOutputOffset;

        private const ConduitType Type = ConduitType.Liquid;

        // The single calibration knob for effectiveness. G = k * footprintArea * PackingFactor,
        // so this folds in plate count and plate thickness (a real plate exchanger packs far
        // more transfer area than its footprint).
        //
        // Calibrated 2026-09-05 at 150 and verified in-game (copper, 10 kg/s brine vs
        // 10 kg/s water: NTU 2.38, eps 0.75, outlet temps matched by hand). Predicted eps
        // for balanced water/water at a full 10 kg/s, refined metals only:
        //   thermium 0.88, aluminum 0.87, copper/gold/tungsten 0.66, iron/steel 0.64, lead 0.53.
        // Dropping to 100 stretches that to roughly aluminum 0.82 / lead 0.43. Effectiveness
        // rises toward 1 for every material as flow drops, so throttling with a valve is the
        // player's lever; material matters most at full throughput.
        private const float PackingFactor = 150f;

        // KMonoBehaviour fills [MyCmpReq] fields by reflection at spawn, so the compiler
        // cannot see the assignment and warns CS0649. Silence it for this field only.
#pragma warning disable CS0649
        [MyCmpReq]
        private Building building;
#pragma warning restore CS0649

        // Per-cell pipe capacity. ConduitFlow keeps its working copy in a private field, but
        // publishes the per-type values as public constants; ours is liquid, fixed above.
        private const float MaxMass = ConduitFlow.MAX_LIQUID_MASS;

        // Stream A primary cells (from the def; already network endpoints).
        private int primaryInputCell;
        private int primaryOutputCell;

        // Stream B secondary cells + endpoints we register ourselves.
        private int secondaryInputCell;
        private int secondaryOutputCell;
        private FlowUtilityNetwork.NetworkItem secondaryInputItem;
        private FlowUtilityNetwork.NetworkItem secondaryOutputItem;

        // G_clean, the conductance of the bare plates (W/K), cached at spawn from the
        // construction material's thermal conductivity. Fouling adds resistance in series.
        private float cleanConductance;

        // Fouling ledgers: kilograms of deposit per byproduct element, one per stream side.
        // SAVED. Thermal resistance is derived from these each tick (Fouling.ResistanceOf).
        [Serialize]
        private Dictionary<SimHashes, float> depositA = new Dictionary<SimHashes, float>();
        [Serialize]
        private Dictionary<SimHashes, float> depositB = new Dictionary<SimHashes, float>();

        // Diagnostic logging for calibration: every LogEveryTicks conduit ticks (1 s each),
        // dump the exchange numbers to Player.log. Flip DebugLog to false for release.
        private const bool DebugLog = true;
        private const int LogEveryTicks = 30;
        private int exchangeTicks;

        protected override void OnSpawn()
        {
            base.OnSpawn();

            // Declaring def.InputConduitType auto-attaches a ConduitConsumer (and the output
            // side may attach a ConduitDispenser). We drive both primary cells by hand, so
            // those components have no Storage to feed and would NRE in Consume on the first
            // packet. Remove them. The def's port icons and network endpoints stay — they
            // come from the def, not these components (same as ConduitBridge, which also
            // never registers its own endpoints).
            RemoveIfPresent<ConduitConsumer>();
            RemoveIfPresent<ConduitDispenser>();

            // Primary cells are rotation-adjusted for us by Building.
            primaryInputCell = building.GetUtilityInputCell();
            primaryOutputCell = building.GetUtilityOutputCell();

            // Secondary cells we resolve by hand, applying rotation like the game does.
            int origin = Grid.PosToCell(transform.GetPosition());
            secondaryInputCell = Grid.OffsetCell(origin, building.GetRotatedOffset(secondaryInputOffset));
            secondaryOutputCell = Grid.OffsetCell(origin, building.GetRotatedOffset(secondaryOutputOffset));

            // The def already registered stream A's primary endpoints. Register stream B's
            // secondary endpoints so pipes connect to them.
            IUtilityNetworkMgr mgr = Conduit.GetNetworkManager(Type);
            secondaryInputItem = new FlowUtilityNetwork.NetworkItem(Type, Endpoint.Sink, secondaryInputCell, gameObject);
            secondaryOutputItem = new FlowUtilityNetwork.NetworkItem(Type, Endpoint.Source, secondaryOutputCell, gameObject);
            mgr.AddToNetworks(secondaryInputCell, secondaryInputItem, true);
            mgr.AddToNetworks(secondaryOutputCell, secondaryOutputItem, true);

            // Effectiveness scales with the material the exchanger is built from: higher
            // thermal conductivity => lower wall resistance => more heat moved per tick.
            // G = k * footprintArea * PackingFactor. footprintArea is the 3x3 footprint.
            PrimaryElement construction = GetComponent<PrimaryElement>();
            float k = construction != null ? construction.Element.thermalConductivity : 0f;
            float footprintArea = building.Def.WidthInCells * building.Def.HeightInCells;
            cleanConductance = k * footprintArea * PackingFactor;

            // Older saves (or a serializer that skipped the field) leave a ledger null.
            depositA = depositA ?? new Dictionary<SimHashes, float>();
            depositB = depositB ?? new Dictionary<SimHashes, float>();

            // Drive flow in phase with the liquid conduit simulation. Note the conduit sim
            // ticks once per SECOND (ConduitFlow.TickRate), not every 200 ms; the solver
            // moves pipe mass first, then calls updaters like this one with dt = 1.0.
            // Whatever we add to an output cell here is carried away by NEXT tick's solve.
            Conduit.GetFlowManager(Type).AddConduitUpdater(ConduitUpdate);
        }

        protected override void OnCleanUp()
        {
            Conduit.GetFlowManager(Type).RemoveConduitUpdater(ConduitUpdate);
            IUtilityNetworkMgr mgr = Conduit.GetNetworkManager(Type);
            mgr.RemoveFromNetworks(secondaryInputCell, secondaryInputItem, true);
            mgr.RemoveFromNetworks(secondaryOutputCell, secondaryOutputItem, true);
            base.OnCleanUp();
        }

        private void RemoveIfPresent<T>() where T : Component
        {
            T c = GetComponent<T>();
            if (c != null)
            {
                Destroy(c);
            }
        }

        private void ConduitUpdate(float dt)
        {
            ConduitFlow flow = Conduit.GetFlowManager(Type);

            // 1. Plan: what will actually move on each stream this tick, given the input's
            //    contents and the output's free capacity. Each stream is planned on its own.
            Packet a = PlanTransfer(flow, primaryInputCell, primaryOutputCell);
            Packet b = PlanTransfer(flow, secondaryInputCell, secondaryOutputCell);

            // 2. Foul: each moving packet deposits on (or scours) its side of the plates.
            //    The wall sits between the streams, so its temperature is estimated as the
            //    mean of both inlets when both flow, or the single inlet otherwise.
            float wall = WallTemperature(a, b);
            Fouling.Apply(ref a, depositA, wall, dt);
            Fouling.Apply(ref b, depositB, wall, dt);

            // 3. Exchange: trade heat between the two moving packets through the fouled
            //    wall. If either stream is stalled this tick, the other passes through
            //    unchanged, like a bridge; an exchanger with one side stopped is just a pipe.
            if (!a.IsEmpty && !b.IsEmpty)
            {
                ExchangeHeat(dt, ref a, ref b, ActualConductance());
            }

            // 4. Commit, bridge-style: add to output, then remove the planned mass from input.
            Commit(flow, primaryInputCell, primaryOutputCell, a);
            Commit(flow, secondaryInputCell, secondaryOutputCell, b);
        }

        private static float WallTemperature(Packet a, Packet b)
        {
            if (!a.IsEmpty && !b.IsEmpty) return 0.5f * (a.Temperature + b.Temperature);
            if (!a.IsEmpty) return a.Temperature;
            return b.Temperature;
        }

        // Clean wall and both deposits are thermal resistances in series:
        //   1/G_actual = 1/G_clean + R_fA + R_fB
        private float ActualConductance()
        {
            if (cleanConductance <= 0f) return 0f;
            float r = 1f / cleanConductance + Fouling.ResistanceOf(depositA) + Fouling.ResistanceOf(depositB);
            return 1f / r;
        }

        // Fraction of total resistance that is deposit: 0 = clean, 0.5 = conductance halved.
        // This is the number the player will eventually see.
        public float FoulingFraction()
        {
            if (cleanConductance <= 0f) return 0f;
            return 1f - ActualConductance() / cleanConductance;
        }

        // Predict this tick's transfer on one stream by applying ConduitFlow.AddElement's own
        // acceptance rule ahead of time. Returns an empty Packet when nothing will move.
        private static Packet PlanTransfer(ConduitFlow flow, int inCell, int outCell)
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

            // AddElement refuses a different element unless the output cell is empty.
            ConduitFlow.ConduitContents dst = flow.GetContents(outCell);
            if (dst.element != src.element && dst.element != SimHashes.Vacuum)
            {
                return p;
            }

            // AddElement caps at the output cell's free space (ConduitContents.GetEffectiveCapacity).
            float capacity = MaxMass - dst.mass;
            float mass = Mathf.Min(src.mass, capacity);
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

        // Move the planned packet: add to the output at its (possibly changed) temperature
        // and mass, then remove the PLANNED mass from the input. The two differ by whatever
        // fouling deposited or returned, so mass is conserved across fluid + deposit.
        // Because PlanTransfer mirrored the acceptance rule, accepted should equal p.Mass; a
        // shortfall means our prediction and the game's rule disagree, so log it loudly.
        private static void Commit(ConduitFlow flow, int inCell, int outCell, Packet p)
        {
            if (p.IsEmpty)
            {
                return;
            }
            float accepted = flow.AddElement(
                outCell, p.Element, p.Mass, p.Temperature, p.DiseaseIdx, p.DiseaseCount);
            if (accepted > 0f)
            {
                flow.RemoveElement(inCell, p.SourceMass);
            }
            if (accepted < p.Mass - 0.001f)
            {
                Debug.LogWarning($"[PCHX] acceptance mismatch: planned {p.Mass:F3} kg, output took {accepted:F3} kg");
            }
        }

        // Counterflow heat exchange between the two moving packets for one tick, by the
        // ε-NTU method. Trades heat, never mass; only the packet temperatures change. Disease
        // and element are untouched.
        private void ExchangeHeat(float dt, ref Packet a, ref Packet b, float conductance)
        {
            if (conductance <= 0f)
            {
                return;
            }

            Element elemA = ElementLoader.FindElementByHash(a.Element);
            Element elemB = ElementLoader.FindElementByHash(b.Element);
            if (elemA == null || elemB == null)
            {
                return;
            }

            // Heat-capacity rate of each stream this tick (energy per kelvin). Mass is in kg
            // but the game's specific heat is per gram, so convert to keep units consistent
            // with the conductance G.
            float cA = a.Mass * 1000f * elemA.specificHeatCapacity;
            float cB = b.Mass * 1000f * elemB.specificHeatCapacity;
            if (cA <= 0f || cB <= 0f)
            {
                return;
            }

            float cMin = Mathf.Min(cA, cB);
            float cMax = Mathf.Max(cA, cB);
            float cr = cMin / cMax;
            float ntu = conductance * dt / cMin;

            // Counterflow effectiveness. The balanced case (cr -> 1) is a removable
            // singularity in the general formula, so handle it on its own.
            float eps;
            if (cr > 0.999f)
            {
                eps = ntu / (1f + ntu);
            }
            else
            {
                float ex = Mathf.Exp(-ntu * (1f - cr));
                eps = (1f - ex) / (1f - cr * ex);
            }
            eps = Mathf.Clamp01(eps);

            float tHot = Mathf.Max(a.Temperature, b.Temperature);
            float tCold = Mathf.Min(a.Temperature, b.Temperature);
            float q = eps * cMin * (tHot - tCold); // energy moved hot -> cold, >= 0

            float aIn = a.Temperature;
            float bIn = b.Temperature;

            // Apply equal-and-opposite energy: what the hot stream loses, the cold gains.
            if (a.Temperature >= b.Temperature)
            {
                a.Temperature -= q / cA;
                b.Temperature += q / cB;
            }
            else
            {
                a.Temperature += q / cA;
                b.Temperature -= q / cB;
            }

            // Periodic sample, not just the first tick: the first packets through a freshly
            // filled pipe are usually partial, so they say little about full-flow behavior.
            if (DebugLog && exchangeTicks++ % LogEveryTicks == 0)
            {
                Debug.Log($"[PCHX] exchange #{exchangeTicks}: dt={dt:F3}s Gclean={cleanConductance:F0} G={conductance:F0} " +
                          $"foul={FoulingFraction() * 100f:F1}% depA={Fouling.TotalMass(depositA):F4}kg depB={Fouling.TotalMass(depositB):F4}kg " +
                          $"A={a.Mass:F3}kg {a.Element} {aIn:F1}K->{a.Temperature:F1}K " +
                          $"B={b.Mass:F3}kg {b.Element} {bIn:F1}K->{b.Temperature:F1}K " +
                          $"cMin={cMin:F0} NTU={ntu:F3} Cr={cr:F3} eps={eps:F3} Q={q:F0}J");
            }
        }

        // ---- ISecondaryInput / ISecondaryOutput ----
        // Explicit implementations: both secondary ports are Liquid, so the interfaces'
        // identically-named methods must resolve to different offsets. The port scanners
        // (BuildingCellVisualizer, BuildingDef) cast to the specific interface, so each
        // gets the right one.

        bool ISecondaryInput.HasSecondaryConduitType(ConduitType type) => type == Type;

        CellOffset ISecondaryInput.GetSecondaryConduitOffset(ConduitType type)
            => type == Type ? secondaryInputOffset : CellOffset.none;

        bool ISecondaryOutput.HasSecondaryConduitType(ConduitType type) => type == Type;

        CellOffset ISecondaryOutput.GetSecondaryConduitOffset(ConduitType type)
            => type == Type ? secondaryOutputOffset : CellOffset.none;
    }
}
