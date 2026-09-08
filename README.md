# Oni-PlateCFHX
A Plate Counterflow Heat Exchanger mod for Oxygen Not Included.

This is a passive 3x3 building that moves heat between two liquid streams without
power, mixing, or storage. Effectiveness depends on the construction material and
flow rate. The plates foul over time and require Duplicant labor to open the pack
and clean the plates. Fouling deposits are dropped as solid debris.

This file describes the building and how a player interacts with it. The models
behind it live in three companion files: [THERMAL.md](THERMAL.md) (heat transfer,
shell heat, melting), [FOULING.md](FOULING.md) (fouling and the liquid table), and
[TESTING.md](TESTING.md) (verification plan, verification record, to-do list).
Comments in the mod source explain engine mechanics at the point of use and point
to these files for the reasoning behind them.

## Contents
- [Design goals](#design-goals)
- [Geometry and ports](#geometry-and-ports)
- [Flow model](#flow-model)
- [Cleaning](#cleaning)
- [Localization](#localization)
- [Build menu, research, and recipe](#build-menu-research-and-recipe)
- [Art](#art)
- [Source map](#source-map)
- [Building and testing](#building-and-testing)
- [References](#references)
- Companion files:
  - [THERMAL.md](THERMAL.md): ε-NTU model and calibration; shell heat, insulation, and melting
  - [FOULING.md](FOULING.md): fouling model, liquid classification, cleaning mechanics
  - [TESTING.md](TESTING.md): verification plan, verification record, to do

## Design goals
- **Physically honest where the game can support it.** Counterflow ε-NTU heat
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

Stream A's ports come free with the def. Setting `InputConduitType` also
auto-attaches a `ConduitConsumer` (plus `RequireInputs`/`RequireOutputs`), which
the config strips from the completed-building prefab because we drive the cells
by hand and the consumer would null-reference without a `Storage`. Stream B's ports
are declared by the core component through the secondary-port interfaces (icons
and placement validation) and registered with the liquid network by the core
itself (connectivity). Thin `ConduitSecondaryInput`/`Output` markers give the
same icons during placement and construction, when the core does not yet exist.

## Flow model
Stateless, bridge-style. Each conduit tick (1 s) the core:

1. **Plans** one packet per stream by mirroring `ConduitFlow.AddElement`'s
   acceptance rule: nothing moves if either cell lacks a pipe, or if the output
   holds a different element; otherwise `min(source mass, free capacity)`.
2. **Checks the plate melt rule** (THERMAL.md, "Melting").
3. **Fouls** each planned packet against its side of the plates (FOULING.md).
4. **Exchanges heat** between the two planned packets, only if both are moving.
   One stalled side turns the device into a pipe for that tick. The exchange is
   counterflow ε-NTU (THERMAL.md, "Thermal model").
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
Modeled on vanilla `DropAllWorkable` (the Empty Storage button). What a clean
does to the fouling ledgers, and why the threshold is the displayed percent, is
in FOULING.md, "Cleaning mechanics" and "Deliberate choices".

- A **Clean Plates / Cancel Cleaning** toggle in the building menu, shown once
  at least a gram has deposited or an order is pending.
- An **automatic order at 50% fouling**, fired on the rising edge only and
  re-armed by a completed clean, so a cancelled automatic order is not re-raised
  every second. The trigger compares the same rounded integer percent the status
  item displays (`HeatExchangerCore.FoulingPercent`), so the order fires the
  second the readout says 50%, not a few ticks later at the exact fraction.
- **Both streams stop while the plates are open.** The conduit updater plans
  nothing, so the input pipes back up exactly as behind a closed valve.
- On completion the ledgers empty into **one debris chunk per byproduct** at the
  building's temperature, and the plates are clean.
- Base work time 30 s, scaled by Duplicant attributes. Opening a real plate pack
  is a shift's work; this is a game.
- The errand is our own chore type, **Clean Plates** (`PCHXChores`), with
  `EmptyStorage`'s chore groups (Basekeeping, Hauling), no urge, and both of its
  priorities copied at runtime. Decompile facts (2026-09-07): `ChoreTypes.Add` is
  private but only wraps `ChoreType`'s public constructor, which registers with
  its parent set, resolves group names through `Db.Get().ChoreGroups.TryGet`, and
  creates the duplicant status item itself, so a type constructed in the
  `Db.Initialize` postfix is fully wired. The implicit-priority counter drops by
  50 per vanilla type, so taking the next slot would rank the errand below Idle;
  copying `EmptyStorage.priority` avoids that. Creation failure falls back to
  `EmptyStorage`. The chore strings carry no placeholders, so nothing depends on
  `Chore.ResolveString`.
- The pending order is saved; the chore object is rebuilt on load.

### Status items
- **Fouling: N%** always, with a short tooltip (fouling levels off with flow,
  cleaning point, each stream's deposits; the full explanation is in the building
  description, which the codex shows). Tooltip lines are kept under about 80
  characters with explicit breaks: the side-panel status tooltip sizes itself to
  its longest line rather than wrapping, and the first paragraph-length version
  ran off both edges of the screen.
- **Cleaning ordered** while a chore is pending.
- Three warning (`BadMinor`) items, rendered as red text with an exclamation icon:
  - **Needs cleaning** when fouling is past the threshold with no order pending
    (the cancelled-order case).
  - **No pipe: &lt;port&gt;**, one item per port, whenever that port cell has no
    pipe segment (the same `HasConduit` test that stops the stream, so warning and
    behaviour agree). Four fixed-text items rather than one with a list because
    the world hover card shows status names only, so the name itself has to say
    which port; the single-item version was uninformative there. The vanilla
    `RequireInputs`/`RequireOutputs` components the def attaches are stripped
    from the prefab with the consumer and dispenser: they covered stream A's
    ports only, the input one went inert when the consumer was destroyed, and
    the output one duplicated ours. Stripping must happen on the prefab, not
    per instance at spawn: `Destroy` is deferred to end of frame, so a
    `RequireInputs` destroyed at spawn still ran its own `OnSpawn` that frame
    and left "No Liquid Intake" and "Liquid Pipe Empty" behind permanently.
    Note for any future status cleanup: `RemoveStatusItem(StatusItem)` throws
    for items created with `allow_multiples = true` (vanilla `NeedLiquidOut` is
    one); those need the Guid from `AddStatusItem`.
  - **Output near phase change** when an outlet leaves within 2 K of its fluid's
    listed freezing or boiling point, held for 5 s after the last hit so it does
    not flicker. The sim only changes an element's state 3 K beyond the listed
    point and then rebounds 1.5 K toward it (Klei's stand-in for latent heat,
    and a hysteresis band so a fluid sitting at the point does not flip state
    every tick), so 2 K past the listed value is 5 K of real headroom. The
    exchanger can push a fluid past a transition and the output pipe then
    breaks under the vanilla rule; we warn rather than clamp, because clamping
    would create heat from nothing. The 2 K margin has not been re-tested since
    it was cut from 5 K (TESTING.md, "To do").

### Flow readout
A second always-on status item, "Flow: A &lt;rate&gt;, B &lt;rate&gt;", so both rates show on
the world hover card without opening the side panel (decided 2026-09-08). Rates
come from two `Game.Instance.accumulators` handles, one per stream, fed the mass
the output cell accepted in `Commit`; that is what actually moved, not the pipe
contents. The game averages each handle over a fixed 3 s window (accumulated ÷ 3,
then reset), so a stopped stream reads zero within one window with no extra calls,
and at the 1 s conduit tick each reading is the mean of exactly three ticks. Rates
are formatted in kilograms always, so the two numbers compare at a glance. The
tooltip names the ports and shows the effectiveness ε of the last tick on which
both streams flowed, or "none (no flow)" when a stream was idle or the plates
were open for cleaning; ε is the number that tells a player what throttling
bought them and what fouling has cost. The open-plates case is a fix that is
written but unbuilt (TESTING.md, "To do").

## Localization
All player-visible text lives in one `LocString` tree, `PCHXStrings.cs`, whose root
class is named `STRINGS` (from decompiled `LocString.CreateLocStringKeys` and
`Localization.RegisterForTranslation`, 2026-09-07).

- `CreateLocStringKeys(type, parent_path)` walks a type's static `LocString`
  fields and nested types and registers each as `parent_path + TypeName + "." +
  ... + FIELD`. With a null parent and a root named `STRINGS`, the keys come out
  as `STRINGS.BUILDINGS.PREFABS.PLATECOUNTERFLOWHEATEXCHANGER.NAME` and so on,
  which are the exact keys the game reads for building text and the
  `StatusItem` constructor reads for status text. Nested class names therefore
  must match those paths letter for letter. Called at mod load, so English is
  always present.
- `RegisterForTranslation(root)` lists our assembly with the translation loader
  and keys the same tree a second time as `PlateCounterflowHeatExchanger.STRINGS.*`,
  the form `.po` files address. Called from a postfix on
  `Localization.Initialize`, followed by `CreateLocStringKeys(root, null)` again
  so translated text lands under the vanilla keys. The patch is applied by hand
  with a null check, so a renamed method degrades to a log warning and English.
- Inside our namespace `STRINGS` shadows the game's class; the one vanilla
  reference (the Aquatuner's energy-source string) is written `global::STRINGS`.
- `LocString` converts implicitly to and from `string`, so most call sites are
  unchanged; a conditional expression mixing the two needs an explicit cast.

Still to do for shipped translations: load `translations/<locale>.po` from the
mod folder between the two calls above, and generate a `.pot` template for
translators. Both need signatures confirmed in decompile:
`Localization.LoadStringsFile`, `Localization.OverloadStrings`,
`Localization.GetLocale` (for the locale code), and
`Localization.GenerateStringsTemplate`. Low priority until someone asks for a
translation.

## Build menu, research, and recipe
- **Category:** Utilities, in the Aquatuner's group, immediately after the
  Aquatuner. That is where a player looking for equipment that moves heat
  through liquids looks. The subcategory tag is `PlanSubcategoryName.temperature`,
  which is the Aquatuner's own entry in `PLANSUBCATEGORYSORTING`; the game
  labels that group "Liquid Tuning" in the build menu. The tag and the relative
  ordering are independent, so the tag must match the neighbor's or the building
  lands in a different group.
- **Research:** Liquid Tuning (tech id `LiquidTemperature`; in `Database.Techs`
  it unlocks the Aquatuner, Liquid Tepidizer, Radiant Pipe, Conduction Panel and
  the liquid pipe sensors), one tier deeper than Improved Plumbing. Improved
  Plumbing (`ImprovedLiquidPiping`) was the first choice because the Plastic
  Gasket unlocks there, but a heat exchanger belongs with the other heat-moving
  equipment. A recipe ingredient does not gate a building on its own; only
  membership in a Tech's unlock list does.
- **Recipe:** refined metal (tier-5 mass) plus 2 Plastic Gaskets plus a third
  slot for an `Insulator` material (tier-3 mass). The Steam Turbine uses the
  metal-and-gasket pairing with 4 gaskets for a 5x3 footprint. Gaskets are
  thematic: cleaning means opening the plate pack. The insulator slot and its
  five materials are in THERMAL.md, "Third construction material: insulation".
- **Art:** custom kanim; see Art. `metalrefinery_kanim` remains the fallback if it fails to load.

## Art
The building uses its own kanim, `plate_counterflow_heat_exchanger`. The game
loads any `anim/assets/<name>/` folder inside a mod and registers it as kanim
`<name>`, so the folder name is the name `CreateBuildingDef` asks for. If the
kanim is missing at def-creation time the config falls back to
`metalrefinery_kanim` and logs `[PCHX] kanim ... not loaded`.

The custom art is wired but has not yet been seen in game. The first in-game
test, with each symptom and its fix, is in TESTING.md, "Verification plan".

**Pipeline.** Source of truth is the Spriter project in `art/kanim-source/`: one
`.scml` and one 384x384 PNG per symbol frame (`<symbol>_<n>.png`; kanimal takes
the symbol name from the file name, which is why the icon file is `ui_0.png`).
`tools/build_kanim.sh` runs kanimal-cli (kanimal-SE 1.3.31) over it and writes
`anim/assets/plate_counterflow_heat_exchanger/` with the files renamed
`<name>_build.bytes`, `<name>_anim.bytes`, `<name>_0.png`. kanimal-cli is a
.NET Core 3.1 + System.Drawing tool, so on this Ubuntu it runs in a small Docker
image (`pchx-kanimal`, built on first use from Microsoft's 3.1 runtime image plus
libgdiplus). The csproj copies `anim/**` into the Dev mod folder. Edit the source,
run the script, rebuild. `art/svg/` keeps the vector originals (static body and
the four glint overlays) the PNG frames were rendered from; the first version of
the art and the Spriter project were produced with Codex on 2026-09-08.

**Contents.** Symbols: `_body` (1 frame), `_glint` (4 frames: pale segments
drift down the fins, copper glints move through the lower manifold), `ui` (a
copy of the body for the plan-menu icon). Anims: `idle` (the original 4-frame
glint loop), `off` (body only; what a completed building shows), `on` (the
glint loop; nothing plays it yet, see TESTING.md, "To do"), `place` (body only;
preview ghost and construction site), `ui` (icon). All frames share pivot
(0.5, 0.0625): centre bottom, 24 px above the tile edge, at the feet.

**Format notes** (BILD v10 / ANIM v5, little-endian, SDBM hashes over the
lower-cased name, a hash-to-name table at the end of each file). Recorded so the
files can be read without kanimal:
- Build symbol frame: `src, dur, img, pivotX, pivotY, pivotW, pivotH, uvX1, uvY1, uvX2, uvY2`.
  kanimal writes pivot `(0, -336, 768, 768)` for a 384-px tile: width and height
  are twice the atlas pixels and the origin sits 24 px above the tile's bottom.
  UV y = 0 is the top of the PNG in kanimal's output.
- Anim frame: `bboxCX, bboxCY, bboxW, bboxH`, then elements
  `symbol, frame, folder, flags, rgba, a b c d tx ty, order`. kanimal writes the
  ANIM header's element and frame totals as 0 and every frame bbox as
  `(192, 192, 384, 384)`; other mods ship this, so the game tolerates it.
- I read the 2x pivot convention as 200 anim units per cell (`animScale`
  0.005), which would put the body at about 3.1 cells tall. Inference; the first
  in-game look settles it. If the size is wrong, set `scale_x`/`scale_y` on the
  sprites in the `.scml` and rebuild.

## Source map
| File | Role |
|---|---|
| `README.md` | This file: the building, ports, flow, cleaning as the player sees it, localization, build menu, art, build instructions |
| `THERMAL.md` | ε-NTU model and `PackingFactor` calibration; shell heat, insulator slot, `ShellFactor` calibration, melt rule |
| `FOULING.md` | Fouling model, τ calibration, temperature factors, liquid classification, byproducts, cleaning mechanics |
| `TESTING.md` | Verification plan, verification record, to do, release checklist |
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
| `art/svg/` | Vector originals the PNG frames were rendered from |
| `tools/build_kanim.sh` | kanimal-cli in Docker: `art/kanim-source/` to `anim/assets/plate_counterflow_heat_exchanger/` (see Art) |
| `anim/` | Generated; do not edit by hand |

## Building and testing
Target `net48` (the game's bundled Harmony is 4.8). The csproj copies the DLL,
yaml files and the `anim/` tree into the game's `mods/Dev/` folder after each
build. Diagnostics go to `Player.log` as `[PCHX]` lines every 30 conduit ticks
while `DebugLog` is true in `HeatExchangerCore`; set it false for release.

Test rigs, the verification record, and the to-do list are in TESTING.md.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface
  fouling. *British Chemical Engineering*, 4(5), 258–262.
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  (ε-NTU method).
- Vanilla reference classes read in decompile for the ports, flow, cleaning, and
  build-menu work: `ConduitBridge`, `ConduitFlow`, `ConduitPreferentialFlow`,
  `GasFilterConfig`, `SteamTurbineConfig2`, `DropAllWorkable`, `Toilet`,
  `ModUtil`, `PlanScreen.PlanInfo`, `ChoreType`, `LocString`, `Localization`.
- THERMAL.md and FOULING.md carry the references for their own models.
