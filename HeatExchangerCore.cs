using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // A tiny value type holding one stream's in-flight fluid between pull and push.
    // Kept as a float buffer (never a real Storage) on purpose: a Storage is a physical
    // object ONI's own thermal sim would conduct heat into and out of, which would fight
    // and double-count the energy balance we compute in C#. A float buffer is invisible
    // to that sim, so the thermal math (added in 3b) is entirely ours.
    public struct LiquidBuffer
    {
        public SimHashes Element;
        public float Mass;
        public float Temperature;
        public byte DiseaseIdx;
        public int DiseaseCount;

        public bool IsEmpty => Mass <= 0f;

        public void Fill(ConduitFlow.ConduitContents c)
        {
            Element = c.element;
            Mass = c.mass;
            Temperature = c.temperature;
            DiseaseIdx = c.diseaseIdx;
            DiseaseCount = c.diseaseCount;
        }

        // Remove what the output pipe accepted; back-pressure leaves the rest for next tick.
        public void Drain(float amount)
        {
            Mass -= amount;
            if (Mass <= 0.0001f)
            {
                Mass = 0f;
                Element = SimHashes.Vacuum;
            }
        }
    }

    // The device core. Drives both liquid streams by hand and, from 3b on, exchanges heat
    // between them (counterflow, ε-NTU, effectiveness set by the construction material's
    // thermal conductivity). The two buffers never share mass; a counterflow exchanger
    // trades heat, not fluid.
    //
    // Stream A rides the building's PRIMARY conduit ports (from the def's
    // Utility{Input,Output}Offset); we read/write those cells manually now instead of via
    // ConduitConsumer/Dispenser/Storage. Stream B rides two SECONDARY ports this component
    // declares (ISecondaryInput/ISecondaryOutput → icons + placement validation) and
    // registers with the liquid network itself.
    //
    // 3a: pure passthrough. No heat yet. The seam for the exchange is marked in ConduitUpdate.
    public class HeatExchangerCore : KMonoBehaviour, ISecondaryInput, ISecondaryOutput
    {
        // Stream B (secondary) offsets, set by the config before spawn. Stream A's offsets
        // live on the BuildingDef, so they are not repeated here.
        public CellOffset secondaryInputOffset;
        public CellOffset secondaryOutputOffset;

        private const ConduitType Type = ConduitType.Liquid;
        private const float BufferCapacityKg = 10f; // one liquid packet's worth

        // The single calibration knob for effectiveness. G = k * footprintArea * PackingFactor,
        // so this folds in plate count and plate thickness (a real plate exchanger packs far
        // more transfer area than its footprint). Tune from the logged dt and observed
        // effectiveness so a good conductor lands high and a poor one clearly lower.
        private const float PackingFactor = 150f;

        [MyCmpReq]
        private Building building;

        // Stream A primary cells (from the def; already network endpoints).
        private int primaryInputCell;
        private int primaryOutputCell;

        // Stream B secondary cells + endpoints we register ourselves.
        private int secondaryInputCell;
        private int secondaryOutputCell;
        private FlowUtilityNetwork.NetworkItem secondaryInputItem;
        private FlowUtilityNetwork.NetworkItem secondaryOutputItem;

        private LiquidBuffer bufferA;
        private LiquidBuffer bufferB;

        // G, the exchanger's overall conductance (energy per second per kelvin), cached at
        // spawn from the construction material's thermal conductivity.
        private float conductance;
        private bool loggedFirstTick;

        protected override void OnSpawn()
        {
            base.OnSpawn();

            // Declaring def.InputConduitType auto-attaches a ConduitConsumer (and the output
            // side may attach a ConduitDispenser). We drive both primary cells by hand, so
            // those components have no Storage to feed and would NRE in Consume on the first
            // packet. Remove them. The def's port icons and network endpoints stay — they
            // come from the def, not these components (same as the Advanced-Electrolyzer's
            // manual output). If stream A stops receiving fluid after this, the input
            // endpoint was actually the consumer's and we register it manually instead.
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
            conductance = k * footprintArea * PackingFactor;

            // Drive flow in phase with the liquid conduit simulation.
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

            PullInput(flow, primaryInputCell, ref bufferA);
            PullInput(flow, secondaryInputCell, ref bufferB);

            // Both inlet packets are now buffered. Trade heat between them before pushing.
            ExchangeHeat(dt);

            PushOutput(flow, primaryOutputCell, ref bufferA);
            PushOutput(flow, secondaryOutputCell, ref bufferB);
        }

        // Pull one packet into the buffer only when it is empty: keeps the buffer to a
        // single element, so there is no in-buffer temperature blending to reason about.
        private static void PullInput(ConduitFlow flow, int inCell, ref LiquidBuffer buf)
        {
            if (!buf.IsEmpty)
            {
                return;
            }
            ConduitFlow.ConduitContents c = flow.GetContents(inCell);
            if (c.mass > 0f)
            {
                float pull = Mathf.Min(BufferCapacityKg, c.mass);
                buf.Fill(flow.RemoveElement(inCell, pull));
            }
        }

        // Push the buffer out, honoring back-pressure and skipping when no output pipe.
        private static void PushOutput(ConduitFlow flow, int outCell, ref LiquidBuffer buf)
        {
            if (buf.IsEmpty || !flow.HasConduit(outCell))
            {
                return;
            }
            float accepted = flow.AddElement(
                outCell, buf.Element, buf.Mass, buf.Temperature, buf.DiseaseIdx, buf.DiseaseCount);
            buf.Drain(accepted);
        }

        // Counterflow heat exchange between the two buffers for one tick, by the ε-NTU
        // method. Trades heat, never mass; only the buffer temperatures change. Disease and
        // element are untouched. No exchange unless both streams carry fluid this tick.
        private void ExchangeHeat(float dt)
        {
            if (bufferA.IsEmpty || bufferB.IsEmpty || conductance <= 0f)
            {
                return;
            }

            Element elemA = ElementLoader.FindElementByHash(bufferA.Element);
            Element elemB = ElementLoader.FindElementByHash(bufferB.Element);
            if (elemA == null || elemB == null)
            {
                return;
            }

            // Heat-capacity rate of each stream this tick (energy per kelvin). Mass is in kg
            // but the game's specific heat is per gram, so convert to keep units consistent
            // with the conductance G.
            float cA = bufferA.Mass * 1000f * elemA.specificHeatCapacity;
            float cB = bufferB.Mass * 1000f * elemB.specificHeatCapacity;
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

            float tHot = Mathf.Max(bufferA.Temperature, bufferB.Temperature);
            float tCold = Mathf.Min(bufferA.Temperature, bufferB.Temperature);
            float q = eps * cMin * (tHot - tCold); // energy moved hot -> cold, >= 0

            // Apply equal-and-opposite energy: what the hot stream loses, the cold gains.
            if (bufferA.Temperature >= bufferB.Temperature)
            {
                bufferA.Temperature -= q / cA;
                bufferB.Temperature += q / cB;
            }
            else
            {
                bufferA.Temperature += q / cA;
                bufferB.Temperature -= q / cB;
            }

            if (!loggedFirstTick)
            {
                loggedFirstTick = true;
                Debug.Log($"[PCHX] first exchange: dt={dt:F3}s conductance={conductance:F1} " +
                          $"cMin={cMin:F1} NTU={ntu:F3} Cr={cr:F3} eps={eps:F3}");
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
