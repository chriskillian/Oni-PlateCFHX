using System.Collections.Generic;
using KSerialization;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // This class is the core device logic. It drives both liquid streams by hand,
    // fouls, and exchanges heat between them. Thermal model: THERMAL.md.
    //
    // Flow follows base game ConduitBridge (read input, add to output, remove what was
    // accepted). Adds one step the bridge does not need: the heat math must know the mass
    // that really moves, so acceptance is predicted first by mirroring ConduitFlow.AddElement's
    // rule, heat is exchanged between the two predicted packets, then both are committed.
    //
    // Stream A uses the def's primary ports; stream B uses secondary ports this component
    // declares (ISecondaryInput/ISecondaryOutput) and registers with the network itself.
    //
    // Klei's serializer is OPT-IN: the attribute below says "save nothing unless marked",
    // and this class saves nothing of its own. The fouling deposits live in two Storage
    // components, which the engine serializes. Everything else is rebuilt in OnSpawn from
    // the building.
    [SerializationConfig(MemberSerialization.OptIn)]
    public class HeatExchangerCore : KMonoBehaviour, ISecondaryInput, ISecondaryOutput
    {
        // Stream B (secondary) offsets, set by the config before spawn. Stream A's offsets
        // live on the BuildingDef, so they are not repeated here.
        public CellOffset secondaryInputOffset;
        public CellOffset secondaryOutputOffset;

        private const ConduitType Type = ConduitType.Liquid;

        // The single calibration knob for heat exchange model effectiveness.
        // G_clean = k * footprintArea * this. See THERMAL.md, "Calibration".
        private const float PackingFactor = 150f;

        // Shell heat loss. G_shell = k_insulator * ShellFactor, where k_insulator is the
        // thermal conductivity of the third construction material (recipe tag "Insulator").
        // Sized so Ceramic (k 0.62) loses roughly 1% of a copper exchanger's duty to the
        // room. THERMAL.md, "Shell heat, insulation, and melting".
        private const float ShellFactor = 1500f;

        // KMonoBehaviour fills [MyCmpReq] fields by reflection at spawn, so the compiler
        // cannot see the assignment and warns CS0649. Silence it for this field only.
#pragma warning disable CS0649
        [MyCmpReq]
        private Building building;
        [MyCmpReq]
        private KBatchedAnimController anim;
#pragma warning restore CS0649

        // Art (ART.md): "on" is the glint loop, "off" the still body. Play restarts the
        // clip, so it is only called when the flowing/idle state changes.
        private static readonly HashedString AnimOn = "on";
        private static readonly HashedString AnimOff = "off";
        private bool animOn; // false matches the def's default state, "off"
        // "working" is the plate jitter played while a duplicant cleans (ART.md). Driven from
        // FoulingCleanWorkable rather than Workable.synchronizeAnims: StandardWorker.StartWork
        // would play the work clips on our controller and lock the duplicant's frames to
        // ours, which needs working_pre/loop/pst in both banks.
        private static readonly HashedString AnimWorking = "working";
        private bool cleaning;

        // Stream A primary cells (from the def, already network endpoints).
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

        // Building shell. Conductance from the fluids to the building body (W/K), the body itself,
        // and the sim handle the Aquatuner uses to hand energy to the structure.
        private float shellConductance;
        private PrimaryElement construction;
        private HandleVector<int>.Handle structureTemperature;

        // Plate melting point, the construction metal's highTemp. When the plate temperature
        // (the fluid mean) reaches it, the building melts like any game structure would
        // through the same public DoMelt the sim calls on body temperature.
        private float meltTemperature;
        private bool melted;

        // Idle bookkeeping for the energy tooltip. After two ticks with no flow, report zero
        // once so the displayed rate resets (AirConditioner does the same on a 2 s timer).
        private int idleShellTicks;
        private bool shellIdleReported;
        private int shellTicks;

        // Fouling deposits, one Storage per stream side, holding the byproducts as real solid
        // chunks. Assigned by the building config on the prefab (WarpConduitSender wires its
        // storages the same way); Unity keeps the sibling reference on each instance. The
        // engine serializes the contents, drops them on deconstruction and melt (F5) and
        // carries their temperature (F6). Not [Serialize]d here: the reference is prefab
        // wiring, not state. Capacity is set in OnSpawn from the construction metal.
        public Storage depositsA;
        public Storage depositsB;

        // Diagnostic logging for calibration. true enables the [PCHX] calibration lines in
        // Player.log, one every LogEveryTicks conduit ticks (1 s each).
        private static readonly bool DebugLog = false;   // readonly, not const, so `if (DebugLog)` compiles without an unreachable-code warning
        private const int LogEveryTicks = 30;
        private int exchangeTicks;

        // Set by FoulingCleanWorkable while a duplicant has the plate pack open. While true
        // the updater plans nothing, so both input pipes back up exactly as they would
        // behind a closed valve. Not saved, a reloaded chore re-raises it when work resumes.
        public bool FlowBlocked { get; set; }

        // Handles for the always-on "Fouling: N%" and "Flow: A .., B .." status items.
        private System.Guid foulingStatus;
        private System.Guid flowStatus;

        // Flow readout. The game's Accumulators average whatever is fed to a handle over a
        // 3 s window (accumulated / 3 every 3 s, then reset), so a stopped stream reads zero
        // within one window with no further calls. Fed the accepted mass in Commit.
        private HandleVector<int>.Handle flowAccumulatorA = HandleVector<int>.InvalidHandle;
        private HandleVector<int>.Handle flowAccumulatorB = HandleVector<int>.InvalidHandle;

        // ε of the most recent tick on which both streams flowed; negative when the last tick
        // had one stream idle (no exchange to report).
        private float lastEffectiveness = -1f;

        // Warning status items. Ports: a pipe is missing
        // at one of the four port cells, so that stream cannot flow. Phase: an outlet is
        // within PhaseMargin of its fluid's freezing or boiling point, and a fluid that
        // changes state in a pipe breaks it under the vanilla rule. Packets under 10% of
        // pipe capacity (1 kg) never change state in a pipe. We still warn on temperature
        // alone, since opening the valve would break the pipe. We warn, never clamp.
        // The phase item is informational (Info icon, no notification): bringing a fluid
        // close to a transition without crossing it is the exchanger's job, not a fault
        // (decision 2026-09-19).
        // The sim transitions an element 3 K BEYOND its listed point and then rebounds 1.5 K
        // back toward it (Klei's latent-heat stand-in and anti-flicker hysteresis), so the
        // real lead time is PhaseMargin + 3 K: 2 K here gives 5 K of true headroom.
        private const float PhaseMargin = 2f;          // K past the listed transition point
        private const int PhaseWarningHoldTicks = 5;   // ticks the warning stays up after the last hit
        private KSelectable selectable;
        private readonly System.Guid[] noPipeStatus = new System.Guid[4]; // PortIndex order
        private System.Guid phaseStatus;
        private int phaseWarningTicks;
        private PhaseState phaseA;
        private PhaseState phaseB;

        // Index into PCHXStatusItems.NoPipe and noPipeStatus.
        private enum PortIndex { AIn = 0, AOut = 1, BIn = 2, BOut = 3 }

        // What the phase status item knows about one outlet, captured on the conduit tick
        // and formatted only when the tooltip renders (F7: no strings per tick).
        private struct PhaseState
        {
            public bool Near;          // within PhaseMargin of a transition
            public bool Boils;         // else freezes
            public SimHashes Element;
            public float Temperature;  // the outlet packet, not the pipe (see RefreshPhaseStatus)
            public float Transition;   // the element's listed lowTemp or highTemp
        }

        // Text behind the phase status item, built on demand by its tooltip callback.
        public string PhaseWarning => FormatPhaseWarning();

        protected override void OnSpawn()
        {
            base.OnSpawn();

            // The vanilla ConduitConsumer / RequireInputs / RequireOutputs that the def's
            // conduit types attach are stripped from the prefab by the config
            // (StripVanillaPlumbing), so no instance ever has them. The def's port icons and
            // network endpoints do not depend on them (same as ConduitBridge).

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

            // Effectiveness scales with the material the exchanger is built from. Higher
            // thermal conductivity means lower wall resistance and more heat moved per tick.
            // G = k * footprintArea * PackingFactor. footprintArea is the 3x3 footprint.
            construction = GetComponent<PrimaryElement>();
            float k = construction != null ? construction.Element.thermalConductivity : 0f;
            float footprintArea = building.Def.WidthInCells * building.Def.HeightInCells;
            cleanConductance = k * footprintArea * PackingFactor;

            // Plates melt at the metal's melting point. Elements with no melt product carry
            // Unobtanium as their transition target and DoMelt ignores them.
            meltTemperature = construction != null ? construction.Element.highTemp : float.MaxValue;

            // Building shell. The insulation's conductivity sets how fast fluid heat reaches the body.
            // The body-to-room leg is done by the game sim (AddBuildingHeatExchange over the footprint).
            shellConductance = InsulatorConductivity() * ShellFactor;
            structureTemperature = GameComps.StructureTemperatures.GetHandle(gameObject);

            // Deposit capacity: the mass at which conductance is 1% of clean, per metal
            // (DEPOSIT_TUNING.md, decision 5A). Storage.capacityKg is advisory in the engine;
            // Fouling.Apply enforces it.
            float capacity = Fouling.CapacityFor(cleanConductance);
            if (depositsA != null) depositsA.capacityKg = capacity;
            if (depositsB != null) depositsB.capacityKg = capacity;

            // Drive flow in phase with the liquid conduit simulation. Note the conduit sim
            // ticks once per SECOND (ConduitFlow.TickRate). The solver moves pipe mass first
            // then calls updaters like this one with dt = 1.0.
            // Whatever we add to an output cell here is carried away by NEXT tick's solve.
            Conduit.GetFlowManager(Type).AddConduitUpdater(ConduitUpdate);

            // The fouling readout. We pass ourselves as the item's data so its string
            // callbacks can read the live ledgers each time the panel refreshes.
            selectable = GetComponent<KSelectable>();
            foulingStatus = selectable.AddStatusItem(PCHXStatusItems.Fouling, this);

            // Flow readout. One accumulator per stream (Add ignores its arguments beyond
            // allocating the slot), and the status item whose callbacks read them.
            flowAccumulatorA = Game.Instance.accumulators.Add("PCHX flow A", this);
            flowAccumulatorB = Game.Instance.accumulators.Add("PCHX flow B", this);
            flowStatus = selectable.AddStatusItem(PCHXStatusItems.Flow, this);
        }

        protected override void OnCleanUp()
        {
            selectable.RemoveStatusItem(foulingStatus);
            selectable.RemoveStatusItem(flowStatus);
            flowAccumulatorA = Game.Instance.accumulators.Remove(flowAccumulatorA);
            flowAccumulatorB = Game.Instance.accumulators.Remove(flowAccumulatorB);
            for (int i = 0; i < noPipeStatus.Length; i++)
            {
                PCHXStatusItems.Toggle(selectable, PCHXStatusItems.NoPipe[i], false, this, ref noPipeStatus[i]);
            }
            PCHXStatusItems.Toggle(selectable, PCHXStatusItems.PhaseChangeRisk, false, this, ref phaseStatus);
            Conduit.GetFlowManager(Type).RemoveConduitUpdater(ConduitUpdate);
            IUtilityNetworkMgr mgr = Conduit.GetNetworkManager(Type);
            mgr.RemoveFromNetworks(secondaryInputCell, secondaryInputItem, true);
            mgr.RemoveFromNetworks(secondaryOutputCell, secondaryOutputItem, true);
            base.OnCleanUp();
        }

        private void ConduitUpdate(float dt)
        {
            // Melted, the object is being destroyed at end of frame. Meanwhile, do nothing.
            if (melted)
            {
                return;
            }

            ConduitFlow flow = Conduit.GetFlowManager(Type);
            RefreshPortStatus(flow);

            // Plate pack open for cleaning. Nothing moves on either stream this tick. The
            // phase warning is still ticked so it can expire while the plates are open.
            if (FlowBlocked)
            {
                lastEffectiveness = -1f; // no exchange this tick (readout says "none")
                RefreshPhaseStatus(default, default);
                SetFlowAnim(false);
                return;
            }

            // 1. Plan: what will actually move on each stream this tick, given the input's
            //    contents and the output's free capacity. Each stream is planned on its own.
            Packet a = ConduitTransfer.Plan(flow, primaryInputCell, primaryOutputCell);
            Packet b = ConduitTransfer.Plan(flow, secondaryInputCell, secondaryOutputCell);

            // 2. Foul: each moving packet deposits on (or scours) its side of the plates.
            //    The wall sits between the streams, so its temperature is estimated as the
            //    mean of both inlets when both flow, or the single inlet otherwise. Coking
            //    responds to the hot end instead, estimated as the hotter flowing inlet.
            float wall = WallTemperature(a, b);
            float hotInlet = HotInletTemperature(a, b);

            //    If the plates reach or exceed the melting point, the building melts. Checked on
            //    inlet temperatures, before any exchange, and independent of insulation.
            //    Nothing is committed, and the fluid stays in the input pipes, which now end at nothing.
            //    See THERMAL.md, "Melting".

            if ((!a.IsEmpty || !b.IsEmpty) && wall >= meltTemperature)
            {
                Melt(wall);
                return;
            }

            Fouling.Apply(ref a, depositsA, wall, hotInlet, dt);
            Fouling.Apply(ref b, depositsB, wall, hotInlet, dt);

            // 3. Exchange: trade heat between the two moving packets through the fouled
            //    wall. If either stream is stalled this tick, the other passes through
            //    unchanged, like a bridge. An exchanger with one side stopped is just a pipe.
            //    A single flowing stream still deposits fouling and still trades heat with the
            //    building's shell. See README.md "What it is".
            if (!a.IsEmpty && !b.IsEmpty)
            {
                ExchangeHeat(dt, ref a, ref b, ActualConductance()); // sets lastEffectiveness
            }
            else
            {
                lastEffectiveness = -1f;
            }

            // 3b. Shell: each moving packet leaks heat to (or draws it from) the building
            //     body through the insulation. The body then exchanges with the room under
            //     the vanilla structure-temperature sim.
            ShellExchange(dt, ref a, ref b);

            // 3c. Warn if either outlet is about to freeze or boil in its pipe.
            RefreshPhaseStatus(a, b);

            // 4. Commit, bridge-style: add to output, then remove the planned mass from input.
            //    What the output accepted is what actually flowed; feed it to the readout.
            float movedA = ConduitTransfer.Commit(flow, primaryInputCell, primaryOutputCell, a);
            float movedB = ConduitTransfer.Commit(flow, secondaryInputCell, secondaryOutputCell, b);
            Game.Instance.accumulators.Accumulate(flowAccumulatorA, movedA);
            Game.Instance.accumulators.Accumulate(flowAccumulatorB, movedB);

            // 5. Animation: glints run while liquid moves through either stream.
            SetFlowAnim(movedA + movedB > 0f);
        }

        private void SetFlowAnim(bool flowing)
        {
            if (flowing == animOn)
            {
                return;
            }
            animOn = flowing;
            if (cleaning)
            {
                return; // Remembered in animOn. Shown when the clean ends
            }
            anim.Play(flowing ? AnimOn : AnimOff, KAnim.PlayMode.Loop);
        }

        // Cleaning overrides the flow anim for its duration, then hands back.
        public void SetCleaningAnim(bool on)
        {
            if (on == cleaning)
            {
                return;
            }
            cleaning = on;
            anim.Play(on ? AnimWorking : (animOn ? AnimOn : AnimOff), KAnim.PlayMode.Loop);
        }

        // ---- Flow readout (read by PCHXStatusItems.Flow) ----

        // Mass per second through each stream, averaged over the game's 3 s window.
        public float FlowRateA => Game.Instance.accumulators.GetAverageRate(flowAccumulatorA);
        public float FlowRateB => Game.Instance.accumulators.GetAverageRate(flowAccumulatorB);

        // ε of the last tick on which both streams flowed, or negative if one was idle.
        public float LastEffectiveness => lastEffectiveness;

        private static float WallTemperature(Packet a, Packet b)
        {
            return CounterflowThermalModel.WallTemperature(!a.IsEmpty, a.Temperature, !b.IsEmpty, b.Temperature);
        }

        private static float HotInletTemperature(Packet a, Packet b)
        {
            return CounterflowThermalModel.HotInletTemperature(!a.IsEmpty, a.Temperature, !b.IsEmpty, b.Temperature);
        }

        // Thermal conductivity of the third construction material. The finished building
        // keeps the chosen element per recipe slot on Deconstructable.constructionElements
        // (BuildingDef.Build fills it, one tag per slot in recipe order; deconstruction
        // returns the exact materials from it). Slot 2 is the insulator.
        //
        // The array can only be short on a building from a pre-insulation version of the
        // mod, which Deconstructable back-fills with the primary element alone; those saves
        // are unsupported (decision 2026-09-16). The remaining guard is against an engine
        // change, not a save. It logs an error and uses the metal's conductivity, so a
        // broken read shows up as a hot shell in the log and in play rather than as a
        // plausible insulated one.
        private float InsulatorConductivity()
        {
            Deconstructable dec = GetComponent<Deconstructable>();
            Tag[] chosen = dec != null ? dec.constructionElements : null;
            Element ins = chosen != null && chosen.Length > 2 ? ElementLoader.GetElement(chosen[2]) : null;
            if (ins == null)
            {
                Debug.LogError($"[PCHX] insulator slot missing on {name} ({(chosen == null ? "no" : chosen.Length.ToString())} construction elements); shell uses the metal's conductivity");
                return construction.Element.thermalConductivity;
            }
            if (DebugLog)
            {
                Debug.Log($"[PCHX] insulator={ins.id} k={ins.thermalConductivity} Gshell={ins.thermalConductivity * ShellFactor:G4}W/K");
            }
            return ins.thermalConductivity;
        }

        // Each moving packet relaxes toward the body temperature through half the shell
        // conductance (one side of the plate pack each). The energy the packets lose is
        // handed to the structure in kilojoules, signed, via the same call the Aquatuner
        // uses for its waste heat. The sim then conducts it to the room. Using the
        // Aquatuner's source string puts the rate in the vanilla energy tooltip.
        private void ShellExchange(float dt, ref Packet a, ref Packet b)
        {
            if (shellConductance <= 0f || construction == null)
            {
                return;
            }

            bool flowing = !a.IsEmpty || !b.IsEmpty;
            if (!flowing)
            {
                idleShellTicks++;
                if (idleShellTicks >= 2 && !shellIdleReported)
                {
                    GameComps.StructureTemperatures.ProduceEnergy(structureTemperature, 0f,
                        global::STRINGS.BUILDING.STATUSITEMS.OPERATINGENERGY.PIPECONTENTS_TRANSFER, dt);
                    shellIdleReported = true;
                }
                return;
            }
            idleShellTicks = 0;
            shellIdleReported = false;

            float tBody = construction.Temperature;
            float g = 0.5f * shellConductance;
            float qA = ShellLoss(dt, g, tBody, ref a);
            float qB = ShellLoss(dt, g, tBody, ref b);
            float joules = qA + qB;

            GameComps.StructureTemperatures.ProduceEnergy(structureTemperature, joules / 1000f,
                global::STRINGS.BUILDING.STATUSITEMS.OPERATINGENERGY.PIPECONTENTS_TRANSFER, dt);

            if (DebugLog && shellTicks++ % LogEveryTicks == 0)
            {
                Debug.Log($"[PCHX] shell #{shellTicks}: Gshell={shellConductance:G4}W/K Tbody={tBody:F1}K " +
                          $"qA={qA:F0}J qB={qB:F0}J -> body {joules / 1000f:F3}kJ");
            }
        }

        // Heat given to the body this tick (in Joules, negative values drawn from the body).
        // Element lookup and unit conversion happen here; the physics, including the clamp
        // that stops the packet overshooting the body temperature, is in
        // CounterflowThermalModel.ShellLoss.
        private static float ShellLoss(float dt, float g, float tBody, ref Packet p)
        {
            if (p.IsEmpty)
            {
                return 0f;
            }
            Element e = ElementLoader.FindElementByHash(p.Element);
            if (e == null)
            {
                return 0f;
            }
            float c = CounterflowThermalModel.HeatCapacity(p.Mass, e.specificHeatCapacity);
            float q = CounterflowThermalModel.ShellLoss(c, p.Temperature, tBody, g, dt, out float tNew);
            p.Temperature = tNew;
            return q;
        }

        // Vanilla melt method, invoked on plate temperature. DoMelt spawns the metal's liquid at
        // its melting point in the building's cell with the metal's mass, posts the
        // "building melted" notification, and destroys the object (deferred, so OnCleanUp
        // runs after this updater returns and the flow manager's list is not modified
        // mid-iteration). DoMelt uses the building's total PrimaryElement mass, so gaskets and
        // insulation become metal too (THERMAL.md, "Shell heat, insulation, and melting").
        private void Melt(float plateTemperature)
        {
            melted = true;
            Debug.Log($"[PCHX] plates at {plateTemperature:F0}K exceed {construction.Element.id} melting point {meltTemperature:F0}K: melting");
            StructureTemperatureComponents.DoMelt(construction);
        }

        // Clean wall and both deposits are thermal resistances in series:
        // 1/G_actual = 1/G_clean + R_fA + R_fB
        private float ActualConductance()
        {
            if (cleanConductance <= 0f) return 0f;
            float r = 1f / cleanConductance + Fouling.ResistanceOf(depositsA) + Fouling.ResistanceOf(depositsB);
            return 1f / r;
        }

        // Fraction of total resistance that is deposit. 0 = clean, 0.5 = conductance halved.
        // This is the number the player sees in the status item.
        public float FoulingFraction()
        {
            if (cleanConductance <= 0f) return 0f;
            return 1f - ActualConductance() / cleanConductance;
        }

        // The integer percent the player sees. The automatic cleaning trigger compares this
        // same value, so the order fires exactly when the readout matches the threshold.
        public int FoulingPercent() => Mathf.RoundToInt(FoulingFraction() * 100f);

        public float DepositMass() => depositsA.MassStored() + depositsB.MassStored();

        // ---- Warnings ----

        // One warning per port with no pipe segment on it. HasConduit is the same test
        // ConduitTransfer.Plan uses to decide a stream cannot move, so warning and behavior agree.
        // Port names in the status text follow the unrotated layout. The building is not
        // rotatable (rotation would swap direction of top and bottom flows, no obvious need).
        private void RefreshPortStatus(ConduitFlow flow)
        {
            SetNoPipe(PortIndex.AIn, !flow.HasConduit(primaryInputCell));
            SetNoPipe(PortIndex.AOut, !flow.HasConduit(primaryOutputCell));
            SetNoPipe(PortIndex.BIn, !flow.HasConduit(secondaryInputCell));
            SetNoPipe(PortIndex.BOut, !flow.HasConduit(secondaryOutputCell));
        }

        private void SetNoPipe(PortIndex port, bool missing)
        {
            int i = (int)port;
            PCHXStatusItems.Toggle(selectable, PCHXStatusItems.NoPipe[i], missing, this, ref noPipeStatus[i]);
        }

        // Outlet temperatures against the fluid's own transition points. The item holds for
        // a few ticks after the last hit so a value hovering at the margin does not flicker.
        // Judged on the outlet packet, not on the mixed contents of the output cell (F3,
        // closed by design 2026-09-19): the packet is what the exchanger produced. Residual
        // liquid warmed or chilled in a blocked pipe is the pipe's problem, as for any pipe.
        private void RefreshPhaseStatus(Packet a, Packet b)
        {
            PhaseState sa = PhaseRisk(a);
            PhaseState sb = PhaseRisk(b);
            if (sa.Near || sb.Near)
            {
                phaseA = sa;
                phaseB = sb;
                phaseWarningTicks = PhaseWarningHoldTicks;
            }
            else if (phaseWarningTicks > 0)
            {
                phaseWarningTicks--;
            }
            PCHXStatusItems.Toggle(selectable, PCHXStatusItems.PhaseChangeRisk, phaseWarningTicks > 0, this, ref phaseStatus);
        }

        private static PhaseState PhaseRisk(Packet p)
        {
            PhaseState s = default;
            if (p.IsEmpty) return s;
            Element e = ElementLoader.FindElementByHash(p.Element);
            if (e == null) return s;
            if (p.Temperature <= e.lowTemp + PhaseMargin)
            {
                s.Near = true;
                s.Boils = false;
                s.Transition = e.lowTemp;
            }
            else if (p.Temperature >= e.highTemp - PhaseMargin)
            {
                s.Near = true;
                s.Boils = true;
                s.Transition = e.highTemp;
            }
            if (s.Near)
            {
                s.Element = p.Element;
                s.Temperature = p.Temperature;
            }
            return s;
        }

        private string FormatPhaseWarning()
        {
            string wa = FormatPhase(STRINGS.UI.PCHX.STREAM_A, phaseA);
            string wb = FormatPhase(STRINGS.UI.PCHX.STREAM_B, phaseB);
            return wa != null && wb != null ? wa + "\n" + wb : wa ?? wb ?? "";
        }

        private static string FormatPhase(string stream, PhaseState s)
        {
            if (!s.Near) return null;
            Element e = ElementLoader.FindElementByHash(s.Element);
            string name = e != null ? e.name : s.Element.ToString();
            string format = s.Boils ? STRINGS.UI.PCHX.PHASE_BOIL : STRINGS.UI.PCHX.PHASE_FREEZE;
            return string.Format(format, stream,
                GameUtil.GetFormattedTemperature(s.Temperature), name, GameUtil.GetFormattedTemperature(s.Transition));
        }

        // Drop both sides' deposits as debris at the given position, each chunk at its own
        // temperature (F6). Chunks under minChunkMass are consumed rather than dropped, so a
        // clean never litters the floor with gram-scale debris.
        public void DropDeposits(Vector3 position, float minChunkMass)
        {
            DropSide(depositsA, position, minChunkMass);
            DropSide(depositsB, position, minChunkMass);
        }

        private static void DropSide(Storage deposits, Vector3 position, float minChunkMass)
        {
            if (deposits == null) return;
            // Consume the small ones first. Walk a copy: consuming removes from the list.
            foreach (GameObject item in new List<GameObject>(deposits.items))
            {
                PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                if (pe != null && pe.Mass < minChunkMass)
                {
                    deposits.ConsumeIgnoringDisease(item);
                }
            }
            deposits.DropAll(position);
        }

        // One line per stream for the status tooltip, e.g. "Stream A: 0.3 kg Salt".
        public string DescribeDeposits()
        {
            return string.Format(STRINGS.UI.PCHX.DEPOSIT_LINE, STRINGS.UI.PCHX.STREAM_A, Describe(depositsA)) + "\n" +
                   string.Format(STRINGS.UI.PCHX.DEPOSIT_LINE, STRINGS.UI.PCHX.STREAM_B, Describe(depositsB));
        }

        private static string Describe(Storage deposits)
        {
            var parts = new List<string>();
            if (deposits != null)
            {
                foreach (GameObject item in deposits.items)
                {
                    PrimaryElement pe = item != null ? item.GetComponent<PrimaryElement>() : null;
                    if (pe == null || pe.Mass < 0.001f) continue;
                    parts.Add(GameUtil.GetFormattedMass(pe.Mass) + " " + pe.Element.name);
                }
            }
            // Explicit cast. LocString converts implicitly both to and from string, which
            // leaves a conditional expression with no single type to pick.
            return parts.Count == 0 ? (string)STRINGS.UI.PCHX.NO_DEPOSITS : string.Join(", ", parts);
        }

        // Counterflow heat exchange between the two moving packets for one tick, by the
        // ε-NTU method. Trades heat, never mass. Disease and element are untouched.
        // This method owns the game side (element lookup, unit conversion, the readout
        // field, the debug log); the physics is in CounterflowThermalModel.Exchange.
        private void ExchangeHeat(float dt, ref Packet a, ref Packet b, float conductance)
        {
            Element elemA = ElementLoader.FindElementByHash(a.Element);
            Element elemB = ElementLoader.FindElementByHash(b.Element);
            if (elemA == null || elemB == null)
            {
                return;
            }

            float cA = CounterflowThermalModel.HeatCapacity(a.Mass, elemA.specificHeatCapacity);
            float cB = CounterflowThermalModel.HeatCapacity(b.Mass, elemB.specificHeatCapacity);

            ExchangeResult r = CounterflowThermalModel.Exchange(cA, a.Temperature, cB, b.Temperature, conductance, dt);
            if (!r.Exchanged)
            {
                return; // as before: lastEffectiveness keeps its previous value
            }

            float aIn = a.Temperature;
            float bIn = b.Temperature;
            a.Temperature = r.TA;
            b.Temperature = r.TB;
            lastEffectiveness = r.Effectiveness;

            // Periodic sample (not just the first tick). The first packets through a freshly
            // filled pipe are usually partial, so they say little about full-flow behavior.
            if (DebugLog && exchangeTicks++ % LogEveryTicks == 0)
            {
                Debug.Log($"[PCHX] exchange #{exchangeTicks}: dt={dt:F3}s Gclean={cleanConductance:F0} G={conductance:F0} " +
                          $"foul={FoulingFraction() * 100f:F1}% depA={depositsA.MassStored():F4}kg depB={depositsB.MassStored():F4}kg " +
                          $"A={a.Mass:F3}kg {a.Element} {aIn:F1}K->{a.Temperature:F1}K " +
                          $"B={b.Mass:F3}kg {b.Element} {bIn:F1}K->{b.Temperature:F1}K " +
                          $"cMin={r.CMin:F0} NTU={r.Ntu:F3} Cr={r.CapacityRatio:F3} eps={r.Effectiveness:F3} Q={r.Heat:F0}J");
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
