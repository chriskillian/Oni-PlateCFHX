using System.Collections.Generic;
using KSerialization;
using UnityEngine;

namespace PlateCounterflowHeatExchanger
{
    // The cleaning job: a duplicant opens the plate pack and the deposits drop as solid
    // chunks. Behavior and reasoning: README.md, "Cleaning the plates".
    //
    // Shape follows vanilla DropAllWorkable ("Empty Storage"): a Workable that owns at most
    // one chore, a user-menu button that toggles it, a status item while the order is
    // pending, and a saved flag so the order survives a reload (the Chore object itself
    // never does; OnSpawn recreates it from the flag).
    [SerializationConfig(MemberSerialization.OptIn)]
    public class FoulingCleanWorkable : Workable, ISim1000ms
    {
        // Displayed fouling percent at which a cleaning chore is raised automatically (rising
        // edge only; see autoArmed). Compared against HeatExchangerCore.FoulingPercent, the
        // same rounded integer the status item shows, so the order fires when the readout
        // says 50%, not a few ticks later at the exact fraction.
        public const int AutoCleanThresholdPercent = 50;

        // Base work time in seconds; duplicant attributes scale it.
        private const float CleanWorkTime = 30f;

        // Deposits lighter than this are not worth a debris chunk.
        private const float MinChunkMass = 0.001f;

        // Saved: an order is pending. The chore is rebuilt from this on load.
        [Serialize]
        private bool markedForClean;

        // Saved: the automatic trigger may fire when fouling next crosses the threshold.
        // Disarmed when it fires; re-armed by a completed clean.
        [Serialize]
        private bool autoArmed = true;

        private Chore chore;
        private System.Guid orderedStatus;
        private System.Guid needsCleaningStatus;
        private bool showButton;

        // Workables want a Prioritizable so the player can set the errand's priority.
        // [MyCmpAdd] adds the component to the prefab if it is missing; the field exists
        // only to carry the attribute (CS0169 "never used"). core is filled by reflection
        // (CS0649 "never assigned"), as in HeatExchangerCore.
#pragma warning disable CS0169, CS0649
        [MyCmpAdd]
        private Prioritizable prioritizable;

        [MyCmpReq]
        private HeatExchangerCore core;
#pragma warning restore CS0169, CS0649

        // The user-menu hook, wired as a static delegate the way vanilla does so the event
        // system can dispatch without allocating per instance. 493375141 is the game's
        // hash for OnRefreshUserMenu.
        private static readonly EventSystem.IntraObjectHandler<FoulingCleanWorkable> OnRefreshUserMenuDelegate =
            new EventSystem.IntraObjectHandler<FoulingCleanWorkable>((component, data) => component.OnRefreshUserMenu(data));

        private Chore Chore
        {
            get => chore;
            set
            {
                chore = value;
                markedForClean = chore != null;
            }
        }

        protected FoulingCleanWorkable()
        {
            // Standard approach cells for a building the duplicant works on from outside.
            SetOffsetTable(OffsetGroups.InvertedStandardTable);
        }

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe(493375141, OnRefreshUserMenuDelegate);
            // Text shown over the duplicant while working.
            workerStatusItem = Db.Get().DuplicantStatusItems.Cleaning;
            // The building's own cleaning clip is driven from the work hooks below via
            // HeatExchangerCore.SetCleaningAnim, not by the worker, so the worker must not
            // touch our controller.
            synchronizeAnims = false;
            SetWorkTime(CleanWorkTime);

            // Duplicant animation, as Disinfectable does: the multitool spray aimed at the
            // building, with its splash effect. The multitool's clips live in the duplicant's
            // default banks, so no overrideAnims is needed; StandardWorker.StartWork hands the
            // animation to MultitoolController instead.
            faceTargetWhenWorking = true;
            multitoolContext = "disinfect";
            multitoolHitEffectTag = "fx_disinfect_splash";

            // Also as Disinfectable does: work speed scales with Tidying and the errand grants
            // Basekeeping experience, like every Klei Basekeeping errand.
            attributeConverter = Db.Get().AttributeConverters.TidyingSpeed;
            attributeExperienceMultiplier = TUNING.DUPLICANTSTATS.ATTRIBUTE_LEVELING.PART_DAY_EXPERIENCE;
            skillExperienceSkillGroup = Db.Get().SkillGroups.Basekeeping.Id;
            skillExperienceMultiplier = TUNING.SKILLS.PART_DAY_EXPERIENCE;
            Prioritizable.AddRef(gameObject);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            showButton = ShouldShowButton();
            if (markedForClean)
            {
                // Reload with an order pending: recreate the chore the save could not hold.
                markedForClean = false;
                OrderClean();
            }
        }

        // ---- Ordering ----

        // Toggle, like Empty Storage: no chore -> create one; chore -> cancel it.
        private void ToggleClean()
        {
            if (Chore == null)
            {
                OrderClean();
            }
            else
            {
                CancelClean();
            }
        }

        public void OrderClean()
        {
            if (Chore != null) return;
            if (DebugHandler.InstantBuildMode)
            {
                OnCompleteWork(null);
                return;
            }
            // Our own chore type (PCHXChores), a copy of EmptyStorage's groups and priorities
            // under the name "Clean Plates"; EmptyStorage itself if creation failed.
            // only_when_operational: false because a passive building has no operational
            // state to wait on.
            ChoreType choreType = PCHXChores.CleanPlates ?? Db.Get().ChoreTypes.EmptyStorage;
            Chore = new WorkChore<FoulingCleanWorkable>(
                choreType, this, null,
                run_until_complete: true, null, null, null,
                allow_in_red_alert: true, null,
                ignore_schedule_block: false, only_when_operational: false);
            RefreshStatusItem();
            RefreshButton();
        }

        private void CancelClean()
        {
            if (Chore == null) return;
            Chore.Cancel("Cleaning cancelled");
            Chore = null;
            GetComponent<KSelectable>().RemoveStatusItem(workerStatusItem);
            ShowProgressBar(show: false);
            RefreshStatusItem();
            RefreshButton();
        }

        // Automatic trigger, checked once a second. Also keeps the "Needs cleaning" warning
        // current, since fouling moves without any order changing.
        public void Sim1000ms(float dt)
        {
            if (autoArmed && Chore == null && core.FoulingPercent() >= AutoCleanThresholdPercent)
            {
                autoArmed = false;
                OrderClean();
            }
            RefreshStatusItem();
            RefreshButton();
        }

        // Where the multitool aims and where fx_disinfect_splash spawns. Workable's default
        // is the building's origin cell (bottom centre of the 3x3), which would put the spray
        // on the floor. MultitoolController reads this for SetTargetPos, UpdateWorkTarget and
        // the hit effect, so one override moves all three. Aim at the plate pack: the centre
        // cell, nudged right because the pack sits under the right post (art/CODEX_GUIDANCE.md:
        // plates x 176..260 with the building centre at x 192, 100 px per cell).
        public override Vector3 GetTargetPoint()
        {
            int centreCell = Grid.OffsetCell(Grid.PosToCell(this), 0, 1);
            Vector3 p = Grid.CellToPosCCC(centreCell, Grid.SceneLayer.BuildingFront);
            p.x += 0.25f;
            return p;
        }

        // ---- Work lifecycle ----

        protected override void OnStartWork(WorkerBase worker)
        {
            base.OnStartWork(worker);
            core.FlowBlocked = true;
            core.SetCleaningAnim(true);
        }

        protected override void OnStopWork(WorkerBase worker)
        {
            base.OnStopWork(worker);
            core.FlowBlocked = false;
            core.SetCleaningAnim(false);
        }

        protected override void OnCompleteWork(WorkerBase worker)
        {
            base.OnCompleteWork(worker);
            core.FlowBlocked = false;
            core.SetCleaningAnim(false); // also reached via OnStopWork; idempotent

            // Deposits become debris, each chunk at its own stored temperature (F6), one chunk
            // per material per side. Chunks under MinChunkMass are consumed, not dropped.
            Vector3 position = Grid.CellToPosCCC(Grid.PosToCell(this), Grid.SceneLayer.Ore);
            core.DropDeposits(position, MinChunkMass);

            autoArmed = true;
            Chore = null;
            RefreshStatusItem();
            RefreshButton();
        }

        // ---- UI ----

        private void OnRefreshUserMenu(object data)
        {
            if (!showButton) return;
            KIconButtonMenu.ButtonInfo button = Chore == null
                ? new KIconButtonMenu.ButtonInfo("action_empty_contents", STRINGS.UI.PCHX.CLEAN_BUTTON, ToggleClean,
                    Action.NumActions, null, null, null, STRINGS.UI.PCHX.CLEAN_BUTTON_TOOLTIP)
                : new KIconButtonMenu.ButtonInfo("action_empty_contents", STRINGS.UI.PCHX.CANCEL_CLEAN_BUTTON, ToggleClean,
                    Action.NumActions, null, null, null, STRINGS.UI.PCHX.CANCEL_CLEAN_BUTTON_TOOLTIP);
            Game.Instance.userMenu.AddButton(gameObject, button);
        }

        // Offer the button once there is something to clean, or while an order is pending.
        private bool ShouldShowButton() => Chore != null || core.DepositMass() >= MinChunkMass;

        private void RefreshButton()
        {
            bool show = ShouldShowButton();
            if (show != showButton)
            {
                showButton = show;
                Game.Instance.userMenu.Refresh(gameObject);
            }
        }

        // "Cleaning ordered" while a chore exists; "Needs cleaning" (yellow) when fouling is
        // past the threshold with no order pending, which is the cancelled-order case.
        private void RefreshStatusItem()
        {
            KSelectable selectable = GetComponent<KSelectable>();
            bool ordered = Chore != null;
            PCHXStatusItems.Toggle(selectable, PCHXStatusItems.CleaningOrdered, ordered, core, ref orderedStatus);
            bool needs = !ordered && core.FoulingPercent() >= AutoCleanThresholdPercent;
            PCHXStatusItems.Toggle(selectable, PCHXStatusItems.NeedsCleaning, needs, core, ref needsCleaningStatus);
        }
    }
}
