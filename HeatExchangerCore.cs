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

    // The device core: drives both liquid streams by hand, fouls, and exchanges heat between
    // them. Thermal model: THERMAL.md.
    //
    // Flow follows vanilla ConduitBridge (read input, add to output, remove what was
    // accepted) plus one step the bridge does not need: the heat math must know the mass
    // that really moves, so acceptance is predicted first by mirroring ConduitFlow.AddElement's
    // rule, heat is exchanged between the two predicted packets, then both are committed.
    //
    // Stream A uses the def's primary ports; stream B uses secondary ports this component
    // declares (ISecondaryInput/ISecondaryOutput) and registers with the network itself.
    //
    // Klei's serializer is OPT-IN: the attribute below says "save nothing unless marked",
    // and the [Serialize] ledgers are the only saved state. Everything else is rebuilt in
    // OnSpawn from the building.
    [SerializationConfig(MemberSerialization.OptIn)]
    public class HeatExchangerCore : KMonoBehaviour, ISecondaryInput, ISecondaryOutput
    {
        // Stream B (secondary) offsets, set by the config before spawn. Stream A's offsets
        // live on the BuildingDef, so they are not repeated here.
        public CellOffset secondaryInputOffset;
        public CellOffset secondaryOutputOffset;

        private const ConduitType Type = ConduitType.Liquid;

        // The single calibration knob for effectiveness: G_clean = k * footprintArea * this.
        // Value and per-metal predictions: THERMAL.md, "Calibration".
        private const float PackingFactor = 150f;

        // Shell heat loss: G_shell = k_insulator * ShellFactor, where k_insulator is the
        // thermal conductivity of the third construction material (recipe tag "Insulator").
        // Sized so Ceramic (k 0.62) loses roughly 1% of a copper exchanger's duty to the
        // room. THERMAL.md, "Shell heat, insulation, and melting".
        private const float ShellFactor = 1500f;

        // Used if the insulator cannot be read from the building (see InsulatorConductivity).
        private const float FallbackInsulatorConductivity = 0.62f; // Ceramic

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

        // Shell: conductance from the fluids to the building body (W/K), the body itself,
        // and the sim handle the Aquatuner uses to hand energy to the structure.
        private float shellConductance;
        private PrimaryElement construction;
        private HandleVector<int>.Handle structureTemperature;

        // Plate melting point: the construction metal's highTemp. When the plate temperature
        // (the fluid mean) reaches it the building melts like any vanilla structure would,
        // through the same public DoMelt the sim calls on body temperature.
        private float meltTemperature;
        private bool melted;

        // Idle bookkeeping for the energy tooltip: after two ticks with no flow, report zero
        // once so the displayed rate resets (AirConditioner does the same on a 2 s timer).
        private int idleShellTicks;
        private bool shellIdleReported;
        private int shellTicks;

        // Fouling ledgers: kilograms of deposit per byproduct element, one per stream side.
        // SAVED. Thermal resistance is derived from these each tick (Fouling.ResistanceOf).
        [Serialize]
        private Dictionary<SimHashes, float> depositA = new Dictionary<SimHashes, float>();
        [Serialize]
        private Dictionary<SimHashes, float> depositB = new Dictionary<SimHashes, float>();

        // Diagnostic logging for calibration. true re-enables the [PCHX] calibration lines in
        // Player.log, one every LogEveryTicks conduit ticks (1 s each).
        private static readonly bool DebugLog = false;   // readonly, not const, so `if (DebugLog)` compiles without an unreachable-code warning
        private const int LogEveryTicks = 30;
        private int exchangeTicks;

        // Set by FoulingCleanWorkable while a duplicant has the plate pack open. While true
        // the updater plans nothing, so both input pipes back up exactly as they would
        // behind a closed valve. Not saved: a reloaded chore re-raises it when work resumes.
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
        // changes state in a pipe breaks it under the vanilla rule. We warn, never clamp.
        // The sim transitions an element 3 K BEYOND its listed point and then rebounds 1.5 K
        // back toward it (Klei's latent-heat stand-in and anti-flicker hysteresis), so the
        // real lead time is PhaseMargin + 3 K: 2 K here gives 5 K of true headroom.
        private const float PhaseMargin = 2f;          // K past the listed transition point
        private const int PhaseWarningHoldTicks = 5;   // ticks the warning stays up after the last hit
        private KSelectable selectable;
        private readonly System.Guid[] noPipeStatus = new System.Guid[4]; // PortIndex order
        private System.Guid phaseStatus;
        private int phaseWarningTicks;

        // Index into PCHXStatusItems.NoPipe and noPipeStatus.
        private enum PortIndex { AIn = 0, AOut = 1, BIn = 2, BOut = 3 }

        // Live text behind the phase warning, read by its status-item tooltip callback.
        public string PhaseWarning { get; private set; } = "";

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

            // Effectiveness scales with the material the exchanger is built from: higher
            // thermal conductivity => lower wall resistance => more heat moved per tick.
            // G = k * footprintArea * PackingFactor. footprintArea is the 3x3 footprint.
            construction = GetComponent<PrimaryElement>();
            float k = construction != null ? construction.Element.thermalConductivity : 0f;
            float footprintArea = building.Def.WidthInCells * building.Def.HeightInCells;
            cleanConductance = k * footprintArea * PackingFactor;

            // Plates melt at the metal's melting point. Elements with no melt product carry
            // Unobtanium as their transition target and DoMelt ignores them; every refined
            // metal has one.
            meltTemperature = construction != null ? construction.Element.highTemp : float.MaxValue;

            // Shell: the insulation's conductivity sets how fast fluid heat reaches the body.
            // The body-to-room leg is the sim's own (AddBuildingHeatExchange over the footprint).
            shellConductance = InsulatorConductivity() * ShellFactor;
            structureTemperature = GameComps.StructureTemperatures.GetHandle(gameObject);

            // Older saves (or a serializer that skipped the field) leave a ledger null.
            depositA = depositA ?? new Dictionary<SimHashes, float>();
            depositB = depositB ?? new Dictionary<SimHashes, float>();

            // Drive flow in phase with the liquid conduit simulation. Note the conduit sim
            // ticks once per SECOND (ConduitFlow.TickRate), not every 200 ms; the solver
            // moves pipe mass first, then calls updaters like this one with dt = 1.0.
            // Whatever we add to an output cell here is carried away by NEXT tick's solve.
            Conduit.GetFlowManager(Type).AddConduitUpdater(ConduitUpdate);

            // The fouling readout. We pass ourselves as the item's data so its string
            // callbacks can read the live ledgers each time the panel refreshes.
            selectable = GetComponent<KSelectable>();
            foulingStatus = selectable.AddStatusItem(PCHXStatusItems.Fouling, this);

            // Flow readout: one accumulator per stream (Add ignores its arguments beyond
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
            // Melted: the object is being destroyed at end of frame; do nothing meanwhile.
            if (melted)
            {
                return;
            }

            ConduitFlow flow = Conduit.GetFlowManager(Type);
            RefreshPortStatus(flow);

            // Plate pack open for cleaning: nothing moves on either stream this tick. The
            // phase warning is still ticked so it can expire while the plates are open.
            if (FlowBlocked)
            {
                lastEffectiveness = -1f; // no exchange this tick; the readout says "none"
                RefreshPhaseStatus(default, default);
                SetFlowAnim(false);
                return;
            }

            // 1. Plan: what will actually move on each stream this tick, given the input's
            //    contents and the output's free capacity. Each stream is planned on its own.
            Packet a = PlanTransfer(flow, primaryInputCell, primaryOutputCell);
            Packet b = PlanTransfer(flow, secondaryInputCell, secondaryOutputCell);

            // 2. Foul: each moving packet deposits on (or scours) its side of the plates.
            //    The wall sits between the streams, so its temperature is estimated as the
            //    mean of both inlets when both flow, or the single inlet otherwise.
            float wall = WallTemperature(a, b);

            //    Plates hotter than the metal's melting point: the building melts. Checked on
            //    inlet temperatures, before any exchange, and independent of insulation (which
            //    wraps the skin, not the plates). Nothing is committed; the fluid stays in
            //    the input pipes, which now end at nothing.
            if ((!a.IsEmpty || !b.IsEmpty) && wall >= meltTemperature)
            {
                Melt(wall);
                return;
            }

            Fouling.Apply(ref a, depositA, wall, dt);
            Fouling.Apply(ref b, depositB, wall, dt);

            // 3. Exchange: trade heat between the two moving packets through the fouled
            //    wall. If either stream is stalled this tick, the other passes through
            //    unchanged, like a bridge; an exchanger with one side stopped is just a pipe.
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
            float movedA = Commit(flow, primaryInputCell, primaryOutputCell, a);
            float movedB = Commit(flow, secondaryInputCell, secondaryOutputCell, b);
            Game.Instance.accumulators.Accumulate(flowAccumulatorA, movedA);
            Game.Instance.accumulators.Accumulate(flowAccumulatorB, movedB);

            // 5. Show it: glints run while liquid moves through either stream.
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
                return; // remembered in animOn; shown when the clean ends
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
            if (!a.IsEmpty && !b.IsEmpty) return 0.5f * (a.Temperature + b.Temperature);
            if (!a.IsEmpty) return a.Temperature;
            return b.Temperature;
        }

        // Thermal conductivity of the third construction material. The finished building
        // keeps the chosen element per recipe slot on Deconstructable.constructionElements
        // (that is how deconstruction returns the exact materials); slot 2 is the insulator.
        // Falls back to Ceramic with a warning rather than to zero, so a wrong read shows up
        // in the log without silently making the shell perfect.
        private float InsulatorConductivity()
        {
            Deconstructable dec = GetComponent<Deconstructable>();
            Tag[] chosen = dec != null ? dec.constructionElements : null;
            if (chosen != null && chosen.Length > 2)
            {
                Element ins = ElementLoader.GetElement(chosen[2]);
                if (ins != null)
                {
                    if (DebugLog)
                    {
                        Debug.Log($"[PCHX] insulator={ins.id} k={ins.thermalConductivity} Gshell={ins.thermalConductivity * ShellFactor:G4}W/K");
                    }
                    return ins.thermalConductivity;
                }
            }
            Debug.LogWarning("[PCHX] could not read the insulator material; assuming Ceramic");
            return FallbackInsulatorConductivity;
        }

        // Each moving packet relaxes toward the body temperature through half the shell
        // conductance (one side of the plate pack each). The energy the packets lose is
        // handed to the structure in kilojoules, signed, via the same call the Aquatuner
        // uses for its waste heat; the sim then conducts it to the room. Using the
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

        // Heat one packet gives to the body this tick (J; negative = drawn from the body).
        // Explicit step, clamped so the packet cannot overshoot the body temperature.
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
            float c = p.Mass * 1000f * e.specificHeatCapacity; // J/K
            if (c <= 0f)
            {
                return 0f;
            }
            float dT = p.Temperature - tBody;
            float q = g * dt * dT;
            float qMax = c * dT; // would bring the packet exactly to tBody
            if (Mathf.Abs(q) > Mathf.Abs(qMax))
            {
                q = qMax;
            }
            p.Temperature -= q / c;
            return q;
        }

        // Vanilla melt, invoked on plate temperature. DoMelt spawns the metal's liquid at
        // its melting point in the building's cell with the metal's mass, posts the
        // "building melted" notification, and destroys the object (deferred, so OnCleanUp
        // runs after this updater returns and the flow manager's list is not modified
        // mid-iteration). DoMelt uses the building's total PrimaryElement mass, so gaskets and insulation become metal too (THERMAL.md, "Shell heat, insulation, and melting").
        private void Melt(float plateTemperature)
        {
            melted = true;
            Debug.Log($"[PCHX] plates at {plateTemperature:F0}K exceed {construction.Element.id} melting point {meltTemperature:F0}K: melting");
            StructureTemperatureComponents.DoMelt(construction);
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
        // This is the number the player sees in the status item.
        public float FoulingFraction()
        {
            if (cleanConductance <= 0f) return 0f;
            return 1f - ActualConductance() / cleanConductance;
        }

        // The integer percent the player sees. The automatic cleaning trigger compares this
        // same value, so the order fires exactly when the readout says the threshold.
        public int FoulingPercent() => Mathf.RoundToInt(FoulingFraction() * 100f);

        public float DepositMass() => Fouling.TotalMass(depositA) + Fouling.TotalMass(depositB);

        // ---- Warnings ----

        // One warning per port with no pipe segment on it. HasConduit is the same test
        // PlanTransfer uses to decide a stream cannot move, so warning and behaviour agree.
        // Port names in the status text follow the unrotated layout; the building is not
        // rotatable.
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

        // Outlet temperatures against the fluid's own transition points. The warning holds for
        // a few ticks after the last hit so a value hovering at the margin does not flicker.
        private void RefreshPhaseStatus(Packet a, Packet b)
        {
            string wa = PhaseRisk(STRINGS.UI.PCHX.STREAM_A, a);
            string wb = PhaseRisk(STRINGS.UI.PCHX.STREAM_B, b);
            string warning = wa != null && wb != null ? wa + "\n" + wb : wa ?? wb;
            if (warning != null)
            {
                PhaseWarning = warning;
                phaseWarningTicks = PhaseWarningHoldTicks;
            }
            else if (phaseWarningTicks > 0)
            {
                phaseWarningTicks--;
            }
            PCHXStatusItems.Toggle(selectable, PCHXStatusItems.PhaseChangeRisk, phaseWarningTicks > 0, this, ref phaseStatus);
        }

        private static string PhaseRisk(string stream, Packet p)
        {
            if (p.IsEmpty) return null;
            Element e = ElementLoader.FindElementByHash(p.Element);
            if (e == null) return null;
            if (p.Temperature <= e.lowTemp + PhaseMargin)
            {
                return string.Format(STRINGS.UI.PCHX.PHASE_FREEZE, stream,
                    GameUtil.GetFormattedTemperature(p.Temperature), e.name, GameUtil.GetFormattedTemperature(e.lowTemp));
            }
            if (p.Temperature >= e.highTemp - PhaseMargin)
            {
                return string.Format(STRINGS.UI.PCHX.PHASE_BOIL, stream,
                    GameUtil.GetFormattedTemperature(p.Temperature), e.name, GameUtil.GetFormattedTemperature(e.highTemp));
            }
            return null;
        }

        // Empty both ledgers and hand back the deposits merged by byproduct, so the cleaner
        // can drop one chunk per material. Mass leaves the ledgers here and reappears as
        // debris in the caller; nothing is created or lost.
        public Dictionary<SimHashes, float> TakeDeposits()
        {
            var taken = new Dictionary<SimHashes, float>();
            MergeInto(taken, depositA);
            MergeInto(taken, depositB);
            depositA.Clear();
            depositB.Clear();
            return taken;
        }

        private static void MergeInto(Dictionary<SimHashes, float> into, Dictionary<SimHashes, float> from)
        {
            foreach (KeyValuePair<SimHashes, float> kv in from)
            {
                into.TryGetValue(kv.Key, out float have);
                into[kv.Key] = have + kv.Value;
            }
        }

        // One line per stream for the status tooltip, e.g. "Stream A: 0.3 kg Salt".
        public string DescribeDeposits()
        {
            return string.Format(STRINGS.UI.PCHX.DEPOSIT_LINE, STRINGS.UI.PCHX.STREAM_A, Describe(depositA)) + "\n" +
                   string.Format(STRINGS.UI.PCHX.DEPOSIT_LINE, STRINGS.UI.PCHX.STREAM_B, Describe(depositB));
        }

        private static string Describe(Dictionary<SimHashes, float> ledger)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<SimHashes, float> kv in ledger)
            {
                if (kv.Value < 0.001f) continue;
                Element e = ElementLoader.FindElementByHash(kv.Key);
                string name = e != null ? e.name : kv.Key.ToString();
                parts.Add(GameUtil.GetFormattedMass(kv.Value) + " " + name);
            }
            // Explicit cast: LocString converts implicitly both to and from string, which
            // leaves a conditional expression with no single type to pick.
            return parts.Count == 0 ? (string)STRINGS.UI.PCHX.NO_DEPOSITS : string.Join(", ", parts);
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
        // Returns the mass the output cell accepted (what actually moved this tick).
        private static float Commit(ConduitFlow flow, int inCell, int outCell, Packet p)
        {
            if (p.IsEmpty)
            {
                return 0f;
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
            return accepted;
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
            lastEffectiveness = eps;

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
