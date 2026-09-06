# Oni-PlateCFHX
A Plate Counterflow Heat Exchanger mod for Oxygen Not Included.

A passive 3x3 building that moves heat between two liquid streams without power,
mixing, or storage. Effectiveness depends on the construction metal and on flow
rate. The plates foul over time and a Duplicant has to open the pack and clean
them, dropping the deposits as solid debris.

This file holds the design intent, the calibration record, and the to-do list.
Source comments explain engine mechanics at the point of use and point here for
the reasoning behind them.

## Contents
- [Design goals](#design-goals)
- [Geometry and ports](#geometry-and-ports)
- [Flow model](#flow-model)
- [Thermal model](#thermal-model)
- [Fouling model](#fouling-model)
- [Cleaning](#cleaning)
- [Build menu, research, and recipe](#build-menu-research-and-recipe)
- [Source map](#source-map)
- [Building and testing](#building-and-testing)
- [Verification record](#verification-record)
- [To do](#to-do)
- [References](#references)

## Design goals
- **Physically honest where the game can carry it.** Counterflow ε-NTU heat
  transfer, conductance set by the real thermal conductivity of the construction
  metal, asymptotic (Kern-Seaton style) fouling with resistances in series. Energy
  and mass are conserved to the packet.
- **Emergent trade-offs instead of scripted ones.** Throttling flow raises
  effectiveness for every material but lets more deposit settle. Better metals
  reach higher effectiveness but are more sensitive to fouling in percentage
  terms. Both fall out of the equations; nothing is special-cased.
- **Invisible to the game's own thermal simulation.** The building holds no mass
  between ticks, so ONI never double-counts heat, and there is nothing to lose on
  save and reload except the fouling ledgers, which are saved.
- **Vanilla components and idioms.** No PLib (it cannot express two ports of the
  same conduit type). The code follows decompiled vanilla patterns, named in the
  source, so it can be compared against the game as it changes.

## Geometry and ports
Two liquid streams, each with its own input and output. Stream A runs along the
bottom row left to right; stream B runs along the top row right to left, so the
streams are geometrically counterflow.

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
auto-attaches a `ConduitConsumer`, which we destroy at spawn because we drive the
cells by hand and it would null-reference without a `Storage`. Stream B's ports
are declared by the core component through the secondary-port interfaces (icons
and placement validation) and registered with the liquid network by the core
itself (connectivity). Thin `ConduitSecondaryInput`/`Output` markers give the
same icons during placement and construction, when the core does not yet exist.

## Flow model
Stateless, bridge-style. Each conduit tick (1 s) the core:

1. **Plans** one packet per stream by mirroring `ConduitFlow.AddElement`'s
   acceptance rule: nothing moves if either cell lacks a conduit, or if the output
   holds a different element; otherwise `min(source mass, free capacity)`.
2. **Fouls** each planned packet against its side of the plates.
3. **Exchanges heat** between the two planned packets, only if both are moving.
   One stalled side turns the device into a pipe for that tick.
4. **Commits**: adds to the output, then removes the planned mass from the input.

Nothing is held between ticks. The earlier float-buffer design was dropped
because unserialized buffers lost mass on save while an output was blocked,
duplicated disease under partial pushes, and pulled invisible mass out of pipes.

Alternating packets of different elements on one stream pass through like a pipe.
Each packet exchanges with its own specific heat, so outlet temperatures alternate
packet by packet. There is no wall thermal inertia; the aquatuner makes the same
simplification.

## Thermal model
Counterflow ε-NTU per tick:

- `C = mass_kg × 1000 × specificHeatCapacity` (ONI specific heat is per gram)
- `NTU = G × dt / C_min`, `C_r = C_min / C_max`, `dt = 1.0` (conduit tick)
- `ε = (1 − e^(−NTU(1−C_r))) / (1 − C_r e^(−NTU(1−C_r)))`, or `NTU/(1+NTU)` when
  balanced
- `Q = ε × C_min × (T_hot − T_cold)`, applied equal and opposite

### Calibration
Clean conductance `G_clean = k × 9 × PackingFactor`, where `k` is the construction
metal's thermal conductivity, 9 is the 3x3 footprint, and `PackingFactor` folds in
plate count and thickness. It is the one calibration knob.

`PackingFactor = 150`, verified in game with copper at full flow (10 kg/s brine vs
10 kg/s water): NTU 2.38, ε 0.75, outlet temperatures matched a hand calculation,
and the brine outlet left hotter than the water outlet, a temperature cross that
only counterflow can produce.

Predicted balanced water/water ε at 10 kg/s, refined metals only:

| Metal | k | ε |
|---|---|---|
| Thermium | 220 | 0.88 |
| Aluminum | 205 | 0.87 |
| Copper, Gold, Tungsten | 60 | 0.66 |
| Iron, Steel | 55 / 54 | 0.64 |
| Lead | 35 | 0.53 |

At 1 kg/s every metal is above 0.92. Dropping the factor to 100 stretches the
spread to roughly Aluminum 0.82 / Lead 0.43 if a wider gap is ever wanted.

ε(NTU) saturates, so high-k metals cluster near 1 and material matters most at
full throughput. That is real: at high NTU the wall stops being the limiting
resistance. Materials are restricted to refined metals because ore-tier metals
(gold amalgam, k≈2) gave ε≈0.06 and made the building useless at the bottom of
the tree.

## Fouling model
Asymptotic, after Kern and Seaton: deposition grows with throughput, shear removal
grows with throughput squared, so the deposit levels off, and levels off lower at
high flow.

Per tick, per stream, for a fluid with a table entry:

- `deposition = rate × f(T_wall) × mass`
- `removal = (mass / 10 kg)² × existing_deposit × dt / τ`
- `T_wall` = mean of the two inlet temperatures (or the single flowing one)

State is a **mass ledger** per stream: kilograms of deposit per byproduct
element, saved with the building. Thermal resistance is derived from mass
(`R = kg × 1e-5 K/W`), so mass is the single source of truth and cleaning can
hand back exactly what was deposited. Resistances add in series with the clean
wall: `1/G = 1/G_clean + R_A + R_B`. The player sees `1 − G/G_clean`.

### Fluids and byproducts
| Fluid | Mechanism | f(T) | Byproduct |
|---|---|---|---|
| Polluted Water | biological | 1 below 345 K (72 °C pasteurization), else 0 | Dirt |
| Salt Water | scaling | max(0, (T − 293)/80) | Salt |
| Brine | scaling | as above, higher rate | Salt |
| Crude Oil | coking | 2^((T − 373)/25) | Refined Carbon |
| Petroleum | coking | as above, lower rate | Sulfur |
| Water, Ethanol | none | | |

Sulfur from petroleum follows the vanilla crude → petroleum → sour gas → sulfur
chain. DLC fluids (Mucin will certainly foul; Naphtha, Resin, Nectar, Phyto Oil are
candidates) are not in the table yet; see To do.

### Pacing
Asymptotic deposit = deposition rate × τ, so scaling every rate up and τ down by
the same factor speeds the whole system up without moving any equilibrium. The
first test ran at τ = 1800 s with rates a third of the current ones: physically
sane, but a throttled brine loop took about 41 cycles to reach 50% fouling.
Factor 3 applied: τ = 600 s, a throttled copper brine loop now reaches 50% in
about 19 cycles, and full-flow brine still settles near 27% at a 322 K wall.

### Deliberate choices
1. **Shear scours only the flowing fluid's own byproduct.** A petroleum packet
   strips sulfur, not the carbon a crude packet left. Mixed streams therefore
   level off near the sum of the individual asymptotes. Accepted; mixed streams
   are rare.
2. **Non-fouling fluids do not scour.** A fouled exchanger cannot be flushed with
   water. This is load-bearing: at τ = 600 s a full-flow flush would scrub the
   plates in about ten minutes and the cleaning chore would never be seen. Scale
   and coke do not rinse off in reality either.
3. **Returned deposit mass becomes the flowing element** (carbon back into
   crude, sulfur into petroleum). A fiction, but mass-conserving and far better
   than spawning debris every tick.
4. **The displayed fouling % is the cleaning threshold.** It is a conductance
   ratio, so a thermium exchanger reads 50% at only 0.34 kg of deposit while its
   effectiveness has barely moved, and lead needs 2.1 kg for the same reading
   while its effectiveness is sensitive to every gram. Real plants clean on
   cleanliness factor too, and using the number the player sees keeps the trigger
   legible.

## Cleaning
Modeled on vanilla `DropAllWorkable` (the Empty Storage button).

- A **Clean Plates / Cancel Cleaning** toggle in the building menu, shown once
  at least a gram has deposited or an order is pending.
- An **automatic order at 50% fouling**, fired on the rising edge only and
  re-armed by a completed clean, so a cancelled automatic order is not re-raised
  every second.
- **Both streams stop while the plates are open.** The conduit updater plans
  nothing, so the input pipes back up exactly as behind a closed valve.
- On completion the ledgers empty into **one debris chunk per byproduct** at the
  building's temperature, and the plates are clean.
- Base work time 30 s, scaled by Duplicant attributes. Opening a real plate pack
  is a shift's work; this is a game.
- The pending order is saved; the chore object is rebuilt on load.

Status items: **Fouling: N%** always, with a tooltip explaining asymptotic
fouling and listing each stream's deposits; **Cleaning ordered** while a chore is
pending.

## Build menu, research, and recipe
- **Category:** Utilities, Temperature group, immediately after the Aquatuner.
  That is where a player looking for heat-moving equipment looks.
- **Research:** Improved Plumbing (`ImprovedLiquidPiping`), where the Plastic
  Gasket unlocks. A recipe ingredient does not gate a building on its own; only
  membership in a Tech's unlock list does.
- **Recipe:** refined metal (tier-5 mass) plus 2 Plastic Gaskets. The Steam
  Turbine uses the same pairing with 4 gaskets for a 5x3 footprint. Gaskets are
  thematic: cleaning means opening the plate pack.
- **Art:** borrowed `metalrefinery_kanim` until custom art exists.

## Source map
| File | Role |
|---|---|
| `Mod.cs` | Mod entry, strings, Db patch: plan-screen placement, research unlock, status-item creation |
| `PlateCounterflowHeatExchangerConfig.cs` | BuildingDef, port offsets, recipe, component wiring |
| `HeatExchangerCore.cs` | Both streams: plan / foul / exchange / commit; secondary ports; fouling ledgers; status item |
| `Fouling.cs` | Fouling table, temperature factors, the per-tick deposition/removal step |
| `FoulingCleanWorkable.cs` | Cleaning errand: button, automatic trigger, work lifecycle, debris |
| `PCHXStatusItems.cs` | The two status items and their string callbacks |
| `mod.yaml`, `mod_info.yaml` | Mod manifest. `supportedContent` is obsolete; omitting the DLC lists means "runs everywhere" |

## Building and testing
Target `net48` (the game's bundled Harmony is 4.8). The csproj copies the DLL and
yaml files into the game's `mods/Dev/` folder after each build. Diagnostics go to
`Player.log` as `[PCHX]` lines every 30 conduit ticks while `DebugLog` is true in
`HeatExchangerCore`; set it false for release.

A thermium exchanger on brine throttled to about 2 kg/s against cold water is the
fastest test rig: it fouls to the 50% threshold in about four cycles.

## Verification record
- Dual same-type streams on one building flow simultaneously without mixing.
- Thermal model matches hand calculation at steady state, full flow, copper;
  energy conserved exactly; temperature cross observed.
- Fouling deposition and removal match the model at full and throttled flow;
  ledgers survive save and reload.
- ×3 pacing and gasket recipe verified; research gate verified.
- Cleaning UI verified: status item and tooltip render, button toggles, errand
  appears and disappears with the order.
- Still to verify: a Duplicant completing a clean (pipes stall, chunk drops,
  fouling to 0%); pending order surviving reload; automatic trigger at 50%;
  Utilities/Temperature menu placement and the subcategory warning gone.

## To do
**Player-facing**
- Mod options menu: a switch to disable fouling entirely, and a slider for
  `PackingFactor` (effectiveness). PLib's options system is the usual route and
  is usable for options alone even though we avoid it for conduits.
- Localization: move all player-visible strings (building text, status items,
  tooltips, button labels) into a `LocString` tree registered for translation so
  non-English players get translated text. Button labels are currently plain
  constants.
- Custom chore type so the errand reads "Clean" rather than "Empty Storage".
- "Needs cleaning" warning status item (yellow, `NotificationType.BadMinor`)
  for the case where fouling is over the threshold but the automatic order was
  cancelled.
- Codex / database entry explaining the exchanger and asymptotic fouling.
- "Input not connected" status for stream A's primary input (destroying the
  auto-attached `ConduitConsumer` removed the vanilla one). Stream B's ports have
  never had one.
- Near-phase-change warning: the exchanger can push a fluid past freezing or
  boiling, and the output pipe then breaks under the vanilla rule. Warn rather
  than clamp.
- Custom art (kanim tooling is Windows-centric; deferred).

**Model**
- DLC fluids in the fouling table (Mucin first). `SimHashes` carries DLC members
  regardless of enabled DLCs, so unconditional entries compile; still owed a
  runtime check that a disabled-DLC element lookup cannot throw.
- Flow-rate readout in the tooltip via the game's `accumulators`, as
  `ConduitBridge` does.

**Release**
- `DebugLog = false`.
- Decide whether to keep the acceptance-mismatch warning in release builds.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface
  fouling. *British Chemical Engineering*, 4(5), 258–262.
- Bott, T. R. (1995). *Fouling of Heat Exchangers*. Elsevier.
- TEMA Standards, fouling resistance tables.
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  (ε-NTU method).
- Refined metal thermal conductivities: oxygennotincluded.wiki.gg, Refined Metal.
- Vanilla reference classes read in decompile: `ConduitBridge`, `ConduitFlow`,
  `ConduitPreferentialFlow`, `GasFilterConfig`, `SteamTurbineConfig2`,
  `DropAllWorkable`, `Toilet`, `ModUtil`, `PlanScreen.PlanInfo`.
