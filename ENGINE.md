# Engine facts

This file is not player-facing documentation. It describes the behaviour of Klei's own code that this mod builds on. An entry here is a fact about the game, independent of the mod design. Everything here would be true for any mod, and all of it had some impact on the mod development.  It is a companion to [DEVELOPMENT.md](DEVELOPMENT.md).

Sources are `ilspycmd` decompiles of `Assembly-CSharp`, read between 2026-09-07 and 2026-09-14 unless a line gives its own date, plus in-game observation on the user's build (U59; Spaced Out off; Frosty, Bionic, Prehistoric, Aquatic on).

Klei can change any of this in a game update, so re-read the decompile before trusting an entry against a newer build.

## Contents
- [Conduits](#conduits)
- [Buildings, sim heat, plan screen, research](#buildings-sim-heat-plan-screen-research)
- [Workables and chores](#workables-and-chores)
- [Build tools](#build-tools)
- [Localization](#localization)
- [Status items, side panel, accumulators](#status-items-side-panel-accumulators)
- [Grid and serialization helpers](#grid-and-serialization-helpers)
- [Classes consulted](#classes-consulted)

## Conduits
- `Conduit.GetFlowManager(ConduitType)` returns the flow manager for a conduit
  type; `Conduit.GetNetworkManager(ConduitType)` returns the network manager.
- The liquid conduit solver ticks once per **second** (`ConduitFlow.TickRate`), not every 200 ms. Each tick it moves pipe mass first, then calls the updaters registered through `AddConduitUpdater` with $\Delta t = 1.0$. `RemoveConduitUpdater` unregisters.
- `ConduitFlow.AddConduitUpdater(Action<float> callback, ConduitFlowPriority priority = ConduitFlowPriority.Default)`. The enum values are `First` $= -100$, `Default` $= 0$, `Dispense` $= 50$, `LastPostUpdate` $= 100$. Updaters are ordered with `List.Sort`, which is unstable among equal priorities, so relative order within one priority is not guaranteed.
- Mass added to an output cell during an updater is carried away by the network solver on the **next** tick. That is why a bridge-style output beats writing into a pipe cell directly.
- `ConduitFlow.SetContents(int cell, ConduitContents contents)` is public, alongside `AddElement` and `RemoveElement`.
- Per-cell pipe capacity is published as a public constant per type
  (`ConduitFlow.MAX_LIQUID_MASS`); the solver keeps its working copy private.
- `ConduitFlow.AddElement(cell, element, mass, temperature, diseaseIdx, diseaseCount)` returns the mass the cell accepted. It refuses a different element unless the destination cell is empty (`SimHashes.Vacuum`), and caps the transfer at the cell's free space (`ConduitContents.GetEffectiveCapacity`).
- `ConduitFlow.RemoveElement(cell, mass)` takes mass off a cell.
  `GetContents(cell)` returns a `ConduitContents` (element, mass, temperature, `diseaseIdx`, `diseaseCount`). `HasConduit(cell)` reports whether the cell holds a pipe segment.
- `ConduitBridge` is the reference pattern for driving cells by hand: read the input, add to the output, remove what the output accepted. Disease rides along in proportion to the mass moved.
- Setting `InputConduitType` on a `BuildingDef` auto-attaches a `ConduitConsumer` plus `RequireInputs` and `RequireOutputs`; the consumer null-references without a `Storage`. Setting `OutputConduitType` attaches no `ConduitDispenser`: the
  completed-building prefab carried none (in game, 2026-09-08).
- `ISecondaryInput`/`ISecondaryOutput` give a component's extra ports their placement icons and placement validation only. Connectivity is separate: the component must register its endpoints as `FlowUtilityNetwork.NetworkItem`s with the network manager and call `RemoveFromNetworks` on cleanup.
- **Element state change in pipes.** The sim changes a fluid's state only 3 K beyond the element's listed freezing or boiling point, then rebounds 1.5 K toward it. That is Klei's stand-in for latent heat plus a hysteresis band. Listed points are Klei's, not real-world: water is 272.5 K to 372.5 K in liquid.yaml (0.65 K below real water; ice and steam match; reason unknown). Only packets over 10% of the pipe's capacity (1 kg for liquid, `ConduitFlow.PERCENT_MAX_MASS_FOR_STATE_CHANGE_DAMAGE`) change state and break the pipe; smaller packets do not change state at all and travel supercooled or superheated until they leave the pipe. The mod's phase warning tests temperature only, so it still shows for sub-1 kg packets; the tooltip says so.

## Buildings, sim heat, plan screen, research
- The sim registers every building once through
  `AddBuildingHeatExchange(extents, primaryElement,
  MassForTemperatureModification, T, def.ThermalConductivity, operating_kw)`. Conduction runs over every footprint cell using the primary element's thermal conductivity times the def multiplier.
- A building body's heat capacity is $0.2 \times$ first-material mass $\times$ that material's specific heat. Later construction slots do not contribute.
- The primary element's **mass** is the sum of every construction slot: an 800 kg metal + 200 kg insulator + $2 \times 50$ kg gasket building carries 1100 kg of primary element.
- `GameComps.StructureTemperatures.GetHandle(gameObject)` yields the body's sim handle. `ProduceEnergy(handle, kJ, source, dt)` takes signed **kilojoules** per call; negative values are allowed, so a mod can pull heat out as well as push it in. The sim clamps a body to 0–10000 K.
- `StructureTemperatureComponents.DoMelt` spawns the primary element's
  `highTempTransitionTarget` at its melting point in the building's origin cell, posts the "building melted" notification, and destroys the building. The sim calls it when the body exceeds the primary element's melting point; a mod may call it directly.
- A recipe slot offers every element carrying the slot's tag, in `buildMenuSort` order. Tags and element constants come from
  `StreamingAssets/elements/solid.yaml` and `liquid.yaml` (conductivities, `highTempTransitionOreId`, `dlcId`, disabled entries).
- `Deconstructable.constructionElements` holds one element per recipe slot, in recipe order. Inferred from the vanilla pattern, not read in decompile; the building reads its insulator correctly in game, which confirms it.
- A comment in solid.yaml says Refined Carbon's insulative property is modeled on carbon-bonded carbon fibre (Mersen Calcarb) and is "not too good" by design; its conductivity of 3.1 is the highest of the five `Insulator` elements.
- `Db.Initialize` runs after buildings are generated and after the plan screen exists, so a postfix on it is the safe point to place a building in the build menu, gate it behind a tech, and create status items and chore types.
- `ModUtil.AddBuildingToPlanScreen` takes the subcategory directly, while the game consults its own `PLANSUBCATEGORYSORTING` table for vanilla buildings. The two must agree or the building lands in a different group. A building's subcategory tag and its relative position in the category are independent settings.
- Tech ids are not display names; read them from `Database.Techs`. The tech tree exists only after `Db.Initialize`. Only membership in a `Tech`'s unlock list gates a building; a recipe ingredient does not gate anything on its own.
- `PlanScreen.PlanInfo` is the structure the plan screen holds per category.

## Workables and chores
- `ChoreTypes.Add` is private, but it only wraps the public `ChoreType` constructor, which registers the type and creates its status item. A chore type built in a `Db.Initialize` postfix is therefore fully wired (2026-09-07).
- `ChoreConsumer.ChooseChore` displaces a Duplicant's current chore only at strictly greater `interruptPriority`, and Idle counts as a chore for this test (2026-09-14). `Database.ChoreTypes` assigns `interruptPriority` to vanilla types before a mod type exists, so a mod type keeps 0 unless it copies the field from a model type; an idle Duplicant will otherwise wait for a gap rather than pick the errand up.
- A `Workable` wants a `Prioritizable` on the same object for the player to set the errand's priority.
- `Workable.synchronizeAnims` true makes `StandardWorker.StartWork` play the work clips on the workable's own controller and lock the Duplicant's frames to it, which requires `working_pre`/`_loop`/`_pst` in both banks (2026-09-14).
- `StandardWorker.StartWork` hands the animation to `MultitoolController` when the workable's `GetAnim` returns a multitool state machine. Multitool clips live in the Duplicant's default banks, so no `Workable.overrideAnims` bank is needed for them.
- `MultitoolController` reads `Workable.GetTargetPoint()` for `SetTargetPos`, for `UpdateWorkTarget`, and for the hit-effect position, so one override moves both the Duplicant's aim and the effect (2026-09-14).
- `Disinfectable` is the reference multitool workable: `faceTargetWhenWorking`, `multitoolContext` `"disinfect"`, `multitoolHitEffectTag` `"fx_disinfect_splash"` (2026-09-14). It also scales its work time by the `TidyingSpeed` attribute converter and grants Basekeeping experience.
- `DropAllWorkable` is the workable behind the vanilla Empty Storage button; `EmptyStorage` is its chore type (chore groups Basekeeping and Hauling, no urge).

## Build tools
- `BaseUtilityBuildTool.CheckForConnection(int cell, string defName, string soundName, ref BuildingCellVisualizer outBcv, bool fireEvents)` compares the cursor cell, for Liquid and Gas, against only `building.GetUtilityInputCell()` (when `def.InputConduitType` matches), `building.GetUtilityOutputCell()` (when `def.OutputConduitType` matches), and an `ElementFilter.GetFilteredCell()`. A match calls `BuildingCellVisualizer.ConnectedEvent(cell)` and plays the `GlobalAssets` sound `"OutletConnected"`, inputs and outputs alike. It never tests `ISecondaryInput`/`ISecondaryOutput` cells, so **every** secondary port in the game, Klei's included, is silent when a pipe is laid onto it (2026-09-14).

## Localization
- `LocString.CreateLocStringKeys(type, parent_path)` walks a type's static `LocString` fields and nested types and registers each as  `parent_path + TypeName + "." + ... + FIELD`. With a null parent and a root type named `STRINGS`, the keys come out as `STRINGS.BUILDINGS.PREFABS.<ID>.NAME` and so on: the exact keys the game reads for building text and the `StatusItem` constructor reads for status text.  Nested class names must match those paths letter for letter (2026-09-07).
- `Localization.RegisterForTranslation(root)` lists the calling assembly with the translation loader and keys the same tree a second time as `<Namespace>.STRINGS.*`, the form `.po` files address (2026-09-07).
- `Localization.Initialize` is where the game picks its language, so a postfix on it is the conventional point for a mod to register its string tree for translation and then re-key the translated text under the vanilla keys.
- `LocString` converts implicitly both to and from `string`. A conditional expression mixing the two has no single type to pick and needs an explicit cast.
- Inside a mod namespace, a root type named `STRINGS` shadows the game's own `STRINGS` class; vanilla references then need `global::STRINGS`.

## Status items, side panel, accumulators
- `KSelectable.AddStatusItem(StatusItem, data)` returns a Guid and passes `data` to the item's string callbacks, which run each time the panel refreshes, so the callbacks can read live state.
- The `StatusItem` constructor takes `(id, prefix, icon, icon_type,  notification_type, allow_multiples, render_overlay)` and its string callbacks are `(string, object) -> string`. Inferred from vanilla usage, not read in decompile; it compiles and renders.
- `RemoveStatusItem(StatusItem)` throws for an item created with  `allow_multiples = true` (vanilla `NeedLiquidOut` is one); such items can only be removed by the Guid returned from `AddStatusItem`.
- The side-panel status tooltip sizes itself to its longest line rather than wrapping, so long lines overrun both edges unless broken explicitly.
- The world hover card shows status item **names** only, not their tooltips. A detail a player must see on hover has to be in the name.
- `Object.Destroy` is deferred to end of frame, so a component destroyed at spawn still runs its own `OnSpawn` that frame and can leave permanent state behind. Unwanted def-attached components must be stripped from the prefab, not per instance.
- `Game.Instance.accumulators.Add(name, owner)` allocates a slot and otherwise ignores its arguments. The game averages a handle over a fixed 3 s window (accumulated $\div\ 3$, then reset), so a series that stops reads zero within one window and every reading is the mean of exactly three seconds.
- A side-panel button is a `KIconButtonMenu.ButtonInfo` with a vanilla icon name (for example `action_empty_contents`), refreshed by the component that owns it.

## Grid and serialization helpers
- `Grid.PosToCell(component)` gives a component's cell; `Grid.OffsetCell(cell,  dx, dy)` walks the grid; `Grid.CellToPosCCC(cell, Grid.SceneLayer.X)` gives a cell's center position on a scene layer (`Grid.SceneLayer.BuildingFront` for building art).
- Klei's serializer is opt-in under `[SerializationConfig(MemberSerialization.OptIn)]`: nothing is saved except members marked `[Serialize]`. Everything else must be rebuilt in `OnSpawn`.
- The game loads any `anim/assets/<name>/` folder inside a mod and registers it as kanim `<name>_kanim`. The rest of the art rules are in ART.md.

## Classes consulted
Read in decompile for this mod: `ConduitBridge`, `ConduitFlow`,
`ConduitPreferentialFlow`, `Conduit`, `GasFilterConfig`, `SteamTurbineConfig2`,
`AirConditioner`, `StructureTemperatureComponents`,
`BuildingTemplates.CreateBuildingDef`, `MonumentTopConfig`, `ThermalBlockConfig`,
`DropAllWorkable`, `Disinfectable`, `Workable`, `StandardWorker`,
`MultitoolController`, `ChoreType`, `ChoreConsumer`, `Database.ChoreTypes`,
`Database.Techs`, `Toilet`, `ModUtil`, `PlanScreen.PlanInfo`,
`BaseUtilityBuildTool`, `LocString`, `Localization`, `Grid`. Data files:
`StreamingAssets/elements/solid.yaml`, `liquid.yaml`.

Not yet read, and blocking work listed in TESTING.md, "To do":
`Localization.LoadStringsFile`, `Localization.OverloadStrings`,
`Localization.GetLocale`, `Localization.GenerateStringsTemplate`, and the codex entry generator.