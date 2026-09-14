# Development

This file is for anyone reading or changing the mod's code: implementation
choices, the decisions engine behaviour forced, and the wiring that makes the
building exist. Players want [README.md](README.md). The models are in
[THERMAL.md](THERMAL.md) and [FOULING.md](FOULING.md), the art pipeline in
[ART.md](ART.md), Klei's own behaviour in [ENGINE.md](ENGINE.md), and the
verification plan, record and to-do list in [TESTING.md](TESTING.md).

## Contents
- [Design goals](#design-goals)
  - [Model decisions](#model-decisions)
- [Geometry and ports](#geometry-and-ports)
- [Flow model](#flow-model)
- [Cleaning](#cleaning)
  - [Status items](#status-items)
  - [Flow readout](#flow-readout)
- [Localization](#localization)
- [Build menu, research, and recipe](#build-menu-research-and-recipe)
- [Art](#art)
- [Source map](#source-map)
- [Building and testing](#building-and-testing)
- [References](#references)

## Design goals
- **Physically honest where the game can support it.** Counterflow $\varepsilon$-NTU heat
  transfer, conductance set by the real thermal conductivity of the construction
  metal, asymptotic (Kern-Seaton style) fouling with resistances in series. Energy
  and mass are conserved to the packet. See THERMAL.md and FOULING.md.
- **Emergent trade-offs instead of scripted ones.** Throttling flow rate raises
  effectiveness for every material but increases fouling as more deposit settles.
  Better metals reach higher effectiveness but are more sensitive to fouling in
  percentage terms. Everything is modeled through equations with no special cases.
- **The fluid is invisible to the game's own thermal simulation.** The building
  holds no fluid mass between ticks, so ONI never double-counts heat, and there is
  nothing to lose on save and reload except the fouling ledgers, which are saved.
  The building body itself is an ordinary sim structure; the shell-heat model
  moves energy into it explicitly and lets the sim carry it to the room (see
  THERMAL.md, "Shell heat, insulation, and melting").
- **Vanilla components and idioms.** No PLib (it cannot express two ports of the
  same conduit type). The code follows decompiled vanilla patterns, named in the
  source, so it can be compared against the game as it changes.

### Model decisions
Kept here so THERMAL.md and FOULING.md carry only the model itself.

- **Refined metals only.** Ore-grade metals were considered and rejected: gold
  amalgam ($k \approx 2$) gives $\varepsilon \approx 0.06$, a useless building. The
  vanilla Conduction Panel allows ore anyway.
- **`PackingFactor` $= 150$.** Dropping it to 100 stretches the per-metal spread to
  roughly Aluminum 0.82 / Lead 0.43, if a wider gap is ever wanted.
- **Recipe masses.** 800 kg of tier-5 refined metal, 2 x 50 kg gaskets, 200 kg of
  insulator (`TIER3[0]`).
- **Insulation does not fail.** Modeling it would contradict the convenient fiction
  that plastic gaskets survive plate temperatures. Gasket-free welded plate packs
  exist in reality; one with chemical cleaning would be a different building.
- **One fouling mechanism per liquid.** A second, biological spec for Polluted
  Brine would need a two-specs-per-liquid structure for a narrow effect: scaling is
  near zero below about 30 °C and biological growth stops above 72 °C, so only cold
  Polluted Brine would change, fouling slowly with Dirt instead of not at all. Open
  to player feedback.
- **Mixed-element streams are accepted as rare.** Shear scours only the flowing
  fluid's own byproduct, so a mixed stream levels off near the sum of the
  individual asymptotes (FOULING.md, "Deliberate choices").

## Geometry and ports
Two liquid streams, each with its own input and output port. Stream A runs along
the bottom row left to right. Stream B runs along the top row right to left, so
the streams are geometrically counterflow.

Vocabulary, used consistently in these files, the code, and the player-facing
text: a **stream** is one of the two fluid paths through the plates, A or B,
including its pair of ports; it is still a stream when nothing is flowing. A
**port** is one of the four cells where a pipe attaches. A **pipe** is the game's
own liquid conduit; the word **conduit** appears only where it is the game API's
term (`ConduitType`, `ConduitFlow`, the conduit tick).

| Port | Offset | Position | Provided by |
|---|---|---|---|
| A input | (-1, 0) | bottom-left | BuildingDef `UtilityInputOffset` |
| A output | (1, 0) | bottom-right | BuildingDef `UtilityOutputOffset` |
| B input | (1, 2) | top-right | `HeatExchangerCore` as `ISecondaryInput` |
| B output | (-1, 2) | top-left | `HeatExchangerCore` as `ISecondaryOutput` |

Offsets are horizontally centered: for a 3-wide building valid x offsets are -1,
0, +1. y is bottom-origin. The four offsets are defined once in
`PlateCounterflowHeatExchangerConfig` and read by everything else.

Stream A's ports come free with the def. The config strips the def-attached
`ConduitConsumer` (plus `RequireInputs`/`RequireOutputs`) from the
completed-building prefab because we drive the cells by hand and the consumer
would null-reference without a `Storage` (ENGINE.md, "Conduits"). Stream B's
ports are declared by the core component through the secondary-port interfaces
(icons and placement validation) and registered with the liquid network by the
core itself (connectivity). Thin `ConduitSecondaryInput`/`Output` markers give
the same icons during placement and construction, when the core does not yet
exist.

Laying a pipe onto a flow B port plays no connect sound, while a flow A port
does. The build tool never tests secondary-port cells, so every secondary port in
the game is silent (ENGINE.md, "Build tools"). Closed 2026-09-14 as cosmetic; no
Harmony patch.

## Flow model
Stateless, bridge-style. Each conduit tick (1 s) the core:

1. **Plans** one packet per stream by mirroring the game's own acceptance rule
   (ENGINE.md, "Conduits"): nothing moves if either cell lacks a pipe, or if the
   output holds a different element; otherwise
   $\min(\text{source mass},\ \text{free capacity})$. The heat math needs the mass
   that will really move, which is why acceptance is predicted rather than
   observed.
2. **Checks the plate melt rule** (THERMAL.md, "Shell heat, insulation, and melting").
3. **Fouls** each planned packet against its side of the plates (FOULING.md).
4. **Exchanges heat** between the two planned packets, only if both are moving.
   One stalled side turns the device into a pipe for that tick. The exchange is
   counterflow $\varepsilon$-NTU (THERMAL.md, "Counterflow ε-NTU").
5. **Trades shell heat** between each flowing packet and the building body
   (THERMAL.md, "Shell heat, insulation, and melting").
6. **Commits**: adds to the output, then removes the planned mass from the input.

Nothing is held between ticks. An earlier float-buffer design was dropped
because unserialized buffers lost mass on save while an output was blocked,
duplicated disease under partial pushes, and pulled invisible mass out of pipes.

Alternating packets of different elements on one stream pass through like a pipe.
Each packet exchanges with its own specific heat, so outlet temperatures alternate
packet by packet. Like the thermal aquatuner, there is no wall thermal inertia.

## Cleaning
Modeled on vanilla `DropAllWorkable` (the Empty Storage button). What a clean does to
the fouling ledgers is in FOULING.md, "Cleaning mechanics" and "Deliberate choices".

- A **Clean Plates / Cancel Cleaning** toggle in the building menu, shown once
  at least a gram has deposited or an order is pending.
- An **automatic order at `AutoCleanThresholdPercent`**, triggered on
  `HeatExchangerCore.FoulingPercent`, the same rounded integer the status item
  displays, so the order fires when the readout says 50% (FOULING.md, "Cleaning
  mechanics").
- `workerStatusItem` is `Db.DuplicantStatusItems.Cleaning`;
  `DuplicantStatusItems.Emptying` is a known-good fallback if a game version drops
  it.
- **Both streams stop while the plates are open.** The conduit updater plans
  nothing, so the input pipes back up exactly as behind a closed valve.
- Base work time 30 s, scaled by the `TidyingSpeed` attribute converter; the errand
  grants Basekeeping skill experience. Both are copied from `Disinfectable`
  (ENGINE.md, "Workables and chores") and are a design choice, marked removable in
  the code: drop those lines for a flat 30 s. Opening a real plate pack is a
  shift's work.
- The errand is our own chore type, **Clean Plates** (`PCHXChores`), with
  `EmptyStorage`'s chore groups and no urge; creation failure falls back to
  `EmptyStorage`. The type is built in the `Db.Initialize` postfix (ENGINE.md,
  "Workables and chores").
- **`priority` and `interruptPriority` are copied from the model.** The implicit
  priority counter would rank Clean Plates below Idle, and an idle Duplicant would
  wait for a gap instead of taking the errand (ENGINE.md, "Workables and chores").
- **The `working` clip plays during a clean.** `FoulingCleanWorkable`
  calls `HeatExchangerCore.SetCleaningAnim`, which plays it on start of work and
  restores `on` or `off` from the remembered flow state on stop or completion.
  `synchronizeAnims` stays false, so we owe the Duplicant no work clips of our own
  (ENGINE.md, "Workables and chores").
- **The Duplicant plays the multitool spray**, from `Disinfectable`'s multitool
  fields; no `overrideAnims` bank is needed. It aims at the plate pack, not the
  origin cell: `FoulingCleanWorkable` overrides `Workable.GetTargetPoint()` to return
  the 3x3 centre cell nudged $0.25$ toward the plates, which moves aim and splash
  together (ENGINE.md, "Workables and chores").
- The pending order is saved; the chore object is rebuilt on load.

### Status items
- **Fouling: N%** always, with a short tooltip (fouling levels off with flow,
  cleaning point, each stream's deposits); the full explanation is in the building
  description, which the codex shows. Neither names which fluids foul, only the
  mechanisms; the full list would not fit a tooltip (FOULING.md, "Deliberate
  choices"). Tooltip lines stay under about 80 characters with explicit breaks,
  because the side panel does not wrap them (ENGINE.md, "Status items, side panel,
  accumulators").
- **Cleaning ordered** while a chore is pending.
- Three warning (`BadMinor`) items, rendered as red text with an exclamation icon:
  - **Needs cleaning** when fouling is past the threshold with no order pending
    (the cancelled-order case).
  - **No pipe: &lt;port&gt;**, one item per port, whenever that port cell has no
    pipe segment (the same `HasConduit` test that stops the stream, so warning and
    behaviour agree). Four fixed-text items rather than one with a list, because the
    name is all the world hover card shows, so it has to say which port. The vanilla
    `RequireInputs`/`RequireOutputs` components the def attaches are stripped from the
    prefab, not per instance, along with the consumer and dispenser: they covered
    stream A's ports only, the input one went inert without the consumer, and the
    output one duplicated ours. A per-instance scrub is not an option (ENGINE.md,
    "Status items, side panel, accumulators").
  - **Output near phase change** when an outlet leaves within 2 K of its fluid's
    listed freezing or boiling point, held 5 s after the last hit so it does not
    flicker. The sim's own transition band puts a further 3 K behind the listed
    point (ENGINE.md, "Conduits"), so 2 K past the listed value is 5 K of real
    headroom. The exchanger can push a fluid past a transition and break the output
    pipe; we warn rather than clamp, since clamping would create heat from nothing.
    The 2 K margin has not been re-tested (TESTING.md, "Verification plan").

### Flow readout
A second always-on status item, "Flow: A &lt;rate&gt;, B &lt;rate&gt;", so both rates show on
the world hover card without opening the side panel (decided 2026-09-08). Rates
come from two `Game.Instance.accumulators` handles, one per stream, fed the mass
the output cell accepted in `Commit`; that is what actually moved, not the pipe
contents. The game's 3 s averaging window (ENGINE.md, "Status items, side panel,
accumulators") means a stopped stream reads zero within one window and each reading
is the mean of exactly three conduit ticks. Rates are always in kilograms, so the two numbers
compare at a glance. The tooltip names the ports and shows the effectiveness
$\varepsilon$ of the last tick on which both streams flowed, or "none (no flow)"
when a stream stopped or the plates were open.

## Localization
All player-visible text lives in one `LocString` tree, `PCHXStrings.cs`, whose root
class is named `STRINGS`, because that name is what makes the generated keys match
the ones the game reads (ENGINE.md, "Localization"). Nested class names therefore
match those key paths letter for letter, down to
`STRINGS.BUILDINGS.PREFABS.PLATECOUNTERFLOWHEATEXCHANGER.NAME`.

- `CreateLocStringKeys(root, null)` is called at mod load, so English text is always
  present.
- `RegisterForTranslation(root)` and a second `CreateLocStringKeys(root, null)` run
  from a postfix on `Localization.Initialize`, so translated text lands under the
  vanilla keys. The patch is applied by hand with a null check, so a renamed method
  degrades to a log warning and English.
- The one vanilla `STRINGS` reference we need (the Aquatuner's energy-source string)
  is written `global::STRINGS`, since our root shadows the game's class.

Shipped translations load `translations/<locale>.po` between the two calls above and
ship a `.pot` template; both wait on signatures still to be read (ENGINE.md,
"Classes consulted").

## Build menu, research, and recipe
- **Category:** Utilities, in the Aquatuner's group, immediately after the
  Aquatuner. That is where a player looking for equipment that moves heat
  through liquids looks. The subcategory tag is `PlanSubcategoryName.temperature`,
  the Aquatuner's own entry, which the game labels "Liquid Tuning"; it is taken from
  the enum rather than spelled out, so a Klei rename is a compile error here. Tag and
  ordering are separate settings (ENGINE.md, "Buildings, sim heat, plan screen,
  research").
- **Research:** Liquid Tuning (tech id `LiquidTemperature`, the Aquatuner's own tech),
  one tier deeper than Improved Plumbing. Improved Plumbing (`ImprovedLiquidPiping`) was
  the first choice because the Plastic Gasket unlocks there, but a heat exchanger belongs
  with the other heat-moving equipment. The unlock is an entry in that tech's list;
  the gasket in the recipe gates nothing (ENGINE.md, "Buildings, sim heat, plan
  screen, research").
- **Recipe:** refined metal (tier-5 mass) plus 2 Plastic Gaskets plus a third
  slot for an `Insulator` material (tier-3 mass). The Steam Turbine uses the
  metal-and-gasket pairing with 4 gaskets for a 5x3 footprint, and its parallel
  construction-mass and material-tag arrays are the pattern this config follows.
  Gaskets are
  thematic: cleaning means opening the plate pack. The insulator slot and its
  five materials are in THERMAL.md, "Third construction material: insulation".

## Art
The custom kanim, its pipeline, and the engine rules it obeys are in [ART.md](ART.md).

## Source map
The seven Markdown files are listed at the top of this file.

| File | Role |
|---|---|
| `Mod.cs` | Mod entry, string registration and localization patch, Db patch: plan-screen placement, research unlock, status-item and chore-type creation |
| `PCHXStrings.cs` | The `STRINGS` LocString tree: every player-visible string |
| `PlateCounterflowHeatExchangerConfig.cs` | BuildingDef, port offsets, three-material recipe, component wiring |
| `HeatExchangerCore.cs` | Both streams: plan / melt check / foul / exchange / shell / commit; secondary ports; fouling ledgers; status item |
| `Fouling.cs` | Fouling table, temperature factors, the per-tick deposition/removal step |
| `FoulingCleanWorkable.cs` | Cleaning errand: button, automatic trigger, work lifecycle, debris |
| `PCHXStatusItems.cs` | The six kinds of status item (nine objects: one per port for the pipe warning), their string callbacks, and the add/remove toggle helper |
| `PCHXChores.cs` | The Clean Plates chore type, built from `EmptyStorage`'s groups and priorities |
| `mod.yaml`, `mod_info.yaml` | Mod manifest. `supportedContent` is obsolete; omitting the DLC lists means "runs everywhere" |
| `art/kanim-source/` | Spriter project (`.scml` + PNG frames): source of truth for the art |
| `art/layers.json`, `art/preview/` | Layer extraction record and composites for the current art |
| `art/CODEX_GUIDANCE.md` | Art rules for Codex distilled from versions 2 through 7 (see ART.md, "Art") |
| `art.old/`, `art.new/` | Retired version 1 SVG sources; Codex's version 2 delivery, kept as received |
| `tools/build_kanim.sh`, `tools/pngtool.py` | kanimal-cli in Docker, `art/kanim-source/` to `anim/assets/PCHX/`; pure-Python RGBA PNG `shift`, `stack`, `ghost`, `plates` and `zoom` (no PIL on the Linux box) |
| `anim/` | Generated; do not edit by hand |

## Building and testing
Target `net48` (the game's bundled Harmony is 4.8). The csproj copies the DLL,
yaml files and the `anim/` tree into the game's `mods/Dev/` folder after each
build. `DebugLog` in `HeatExchangerCore` is `readonly`, not `const`, so the
guarded blocks compile without a warning; TESTING.md, "Test rigs" covers its use.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface
  fouling. *British Chemical Engineering*, 4(5), 258–262.
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  ($\varepsilon$-NTU method).
- Vanilla classes read in decompile, and what each one told us: ENGINE.md,
  "Classes consulted".
- THERMAL.md and FOULING.md carry the references for their own models.
