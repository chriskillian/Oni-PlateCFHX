# Oni-PlateCFHX
A Plate Counterflow Heat Exchanger mod for Oxygen Not Included.

This is a passive 3x3 building that moves heat between two liquid streams without
power, mixing, or storage. Effectiveness depends on the construction material and
flow rate. The plates foul over time and requiring Duplicant labor to open the pack
and clean the plates. Fouling deposits are dropped as solid debris.

This file holds the design intent, the calibration record, and the to-do list.
Comments in the mod source explain engine mechanics at the point of use and point
here for the reasoning behind them.

## Contents
- [Design goals](#design-goals)
- [Geometry and ports](#geometry-and-ports)
- [Flow model](#flow-model)
- [Thermal model](#thermal-model)
- [Fouling model](#fouling-model)
- [Cleaning](#cleaning)
- [Shell heat, insulation, and melting](#shell-heat-insulation-and-melting)
- [Localization](#localization)
- [Build menu, research, and recipe](#build-menu-research-and-recipe)
- [Source map](#source-map)
- [Building and testing](#building-and-testing)
- [Verification record](#verification-record)
- [To do](#to-do)
- [References](#references)

## Design goals
- **Physically honest where the game can support it.** Counterflow ε-NTU heat
  transfer, conductance set by the real thermal conductivity of the construction
  metal, asymptotic (Kern-Seaton style) fouling with resistances in series. Energy
  and mass are conserved to the packet.
- **Emergent trade-offs instead of scripted ones.** Throttling flow rate raises
  effectiveness for every material but increases fouling as more deposit settle.
  Better metals reach higher effectiveness but are more sensitive to fouling in
  percentage terms. Everything is modeled through equations with no special cases.
- **The fluid is invisible to the game's own thermal simulation.** The building
  holds no fluid mass between ticks, so ONI never double-counts heat, and there is
  nothing to lose on save and reload except the fouling ledgers, which are saved.
  The building body itself is an ordinary sim structure; the planned shell-heat
  model moves energy into it explicitly and lets the sim carry it to the room.
- **Vanilla components and idioms.** No PLib (it cannot express two ports of the
  same conduit type). The code follows decompiled vanilla patterns, named in the
  source, so it can be compared against the game as it changes.

## Geometry and ports
Two liquid streams, each with its own input and output port. Stream A runs along
the bottom row left to right. Stream B runs along the top row right to left, so
the streams are geometrically counterflow.

Vocabulary, used consistently in the README, the code, and the player-facing
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
   acceptance rule: nothing moves if either cell lacks a conduit, or if the output
   holds a different element; otherwise `min(source mass, free capacity)`.
2. **Fouls** each planned packet against its side of the plates.
3. **Exchanges heat** between the two planned packets, only if both are moving.
   One stalled side turns the device into a pipe for that tick.
4. **Commits**: adds to the output, then removes the planned mass from the input.

Nothing is held between ticks. An earlier float-buffer design was dropped
because unserialized buffers lost mass on save while an output was blocked,
duplicated disease under partial pushes, and pulled invisible mass out of pipes.

Alternating packets of different elements on one stream pass through like a pipe.
Each packet exchanges with its own specific heat, so outlet temperatures alternate
packet by packet. Like the thermal aquatumer, there is no wall thermal inertia.

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
plate count and thickness. This is the only thermal calibration knob. The fouling
model has its own gameplay knob; see [Fouling model, Calibration](#calibration-1).

`PackingFactor = 150`, verified in game with copper at full flow (10 kg/s brine vs
10 kg/s water): NTU 2.38, ε 0.75, outlet temperatures matched a hand calculation,
and the brine outlet left hotter than the water outlet (i.e. successful counterflow
temperature cross).

Predicted balanced water/water ε at 10 kg/s, refined metals only:

| Metal | k | ε |
|---|---|---|
| Thermium | 220 | 0.88 |
| Aluminum | 205 | 0.87 |
| Copper, Gold, Tungsten | 60 | 0.66 |
| Iron, Steel | 55 / 54 | 0.64 |
| Lead | 35 | 0.53 |

At 1 kg/s every metal is above 0.92. Dropping the packing factor to 100 stretches the
spread to roughly Aluminum 0.82 / Lead 0.43 if a wider gap is ever wanted.

ε(NTU) saturates, so high-k metals cluster near 1 and material matters most at
full throughput. At high NTU the wall stops being the limiting resistance.
Materials are restricted to refined metals because ore-tier metals (gold amalgam, k≈2)
gave ε≈0.06 and made the building useless. In-game, building a conduction panel from
gold amalgam is similarly useless, but still allowed.

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

### Calibration
The fouling model has one gameplay knob: the removal time constant **τ**
(`Fouling.RemovalTimeConstant`, currently 600 s). τ sets how fast the deposit
approaches its asymptote, and therefore how many cycles pass before a player sees
the cleaning chore. It does not set where the asymptote lands.

The other constants are physical, not gameplay:

| Constant | Value | Role |
|---|---|---|
| `DepositionRate` (per fluid) | table below | kg of deposit per kg of fluid, before `f(T)` |
| `ResistancePerKg` | 1e-5 K/W per kg | converts ledger mass to thermal resistance |
| `ReferenceMassPerTick` | 10 kg | full-pipe flow; shear removal scales with `(mass / 10 kg)²` |

Setting deposition equal to removal gives the asymptotic deposit
`rate × f(T) × τ × 10 kg / flowFraction` (0.46 kg for full-flow brine at a 322 K
wall, as observed), so rate and τ are not independent. Change τ alone to move pacing and equilibrium together; change rate
and τ by reciprocal factors to move pacing while holding every equilibrium fixed.
The ×3 pacing change below is the second kind. The cleaning threshold
(`AutoCleanThresholdPercent`, 50) is a separate gameplay constant on the workable and
is discussed under Deliberate choices, item 4.

### Fluids and byproducts
Five temperature factors, wall temperature T in kelvin:

| Mechanism | f(T) | Shape |
|---|---|---|
| biological | 1 below 345 K (72 °C pasteurization), else 0 | film grows until pasteurized |
| scaling | max(0, (T − 293)/80) | inverse-solubility salts, rises with T |
| coking | 2^((T − 373)/25) | Arrhenius stand-in, doubles every 25 K |
| particulate | 1 | suspended solids settle regardless of T |
| waxing | clamp01((323 − T)/60) | wax comes out on a cold wall; full at −10 °C, none at 50 °C |

### Liquid classification
Every liquid in the game's `elements/liquid.yaml` (52 entries, build U59, checked
2026-09-08) was classified. Where the yaml names a solid that the liquid leaves
behind on boiling (`highTempTransitionOreId`), that solid is the byproduct: the
game already says what comes out of the liquid. Ids are the yaml `elementId`;
several DLC liquids display under a different name (Brackene = `Milk`, Ovolene =
`FishMilk`, Nectar = `SugarWater`, Mucin = `Mucus`, Polluted Brine = `MurkyBrine`;
the `MilkFat` byproduct displays as Brackwax). Rates are kg deposit per kg fluid
at f(T) = 1.

| Liquid (id) | Mechanism | Byproduct | Rate | Basis |
|---|---|---|---|---|
| Polluted Water (`DirtyWater`) | biological | Dirt | 1.5e-4 | boils to Dirt 1% |
| Mucin (`Mucus`) | biological | Slime | 3e-4 | boils to Polluted Water + Slime 30% |
| Salt Water | scaling | Salt | 6e-5 | Salt 7% |
| Brine | scaling | Salt | 2.1e-4 | Salt 30% |
| Polluted Brine (`MurkyBrine`) | scaling | Salt | 2.1e-4 | Salt 30%; dual-mechanism, scaling chosen by mass |
| Nectar (`SugarWater`) | scaling | Sucrose | 2e-4 | Sucrose 77%; sugar crystallizes on hot surfaces |
| Crude Oil | coking | Refined Carbon | 3e-4 | |
| Petroleum | coking | Sulfur | 6e-5 | vanilla crude → petroleum → sour gas → sulfur chain |
| Naphtha | coking | Refined Carbon | 6e-5 | hydrocarbon, no ore field; coke is the default |
| Gunk (`LiquidGunk`) | coking | Sulfur | 3e-4 | boils to Petroleum + Sulfur 8% |
| Phyto Oil | coking | Algae | 1.5e-4 | boils to CO2 + Algae 67% at 75 °C, so f≈0.5 |
| Biodiesel (`RefinedLipid`) | coking | Refined Carbon | 6e-5 | lipid gumming, low |
| Resin | coking | Isoresin | 3e-4 | Isoresin 25%; the game cures resin with heat |
| Natural Resin | coking | Refined Carbon | 3e-4 | Refined Carbon 25% |
| Latex | coking | Rubber | 2e-4 | latex coagulates with heat |
| Ink | particulate | Refined Carbon | 1e-4 | pigment; boils to Mucus + Refined Carbon 10% |
| Brackene (`Milk`) | waxing | Brackwax (`MilkFat`) | 1.5e-4 | boils to Brine + wax 10% |
| Ovolene (`FishMilk`) | waxing | Brackwax (`MilkFat`) | 1.5e-4 | boils to Steam + wax 10% |

No entry, and why: Water, Ethanol, Super Coolant, Visco-Gel (pure or engineered);
Liquid Sulfur, Liquid Phosphorus, Mercury, Molten Sucrose, every molten metal,
Molten Glass, Molten Salt, Liquid Carbon (single substances); Magma and Liquid
Uranium (the melt rule's territory); Nuclear Waste (a radioactive sludge deposit
would be an invention, not physics; decided 2026-09-08); Chlorine and the
cryogens Oxygen, Hydrogen, Methane, Carbon Dioxide, Propane (nothing dissolved);
Liquid Helium and Molten Syngas are disabled in the yaml.

Only two liquids carry a Spaced Out `dlcId` in the yaml (Liquid Uranium, Nuclear
Waste); every other DLC liquid is in the base file unmarked, so availability is
decided by world generation, and a table entry for an absent liquid is harmless.
The cleaning spawn skips a byproduct whose element lookup fails.

Waxing reference: Bott, *Fouling of Heat Exchangers* (1995), solidification
fouling; paraffin deposition in crude pipelines is the textbook cold-wall case.

### Pacing
Asymptotic deposit = deposition rate × τ, so scaling every rate up and τ down by
the same factor speeds the whole system up without moving any equilibrium. The
first test ran at τ = 1800 s with rates a third of the current ones: physically
sane, but a throttled brine loop took about 58 cycles to reach 50% fouling.
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

Status items: **Fouling: N%** always, with a short tooltip (fouling levels off
with flow, cleaning point, each stream's deposits; the full explanation is in the
building description, which the codex shows). Tooltip lines are kept under about
80 characters with explicit breaks: the side-panel status tooltip sizes itself to
its longest line rather than wrapping, and the first paragraph-length version ran
off both edges of the screen (check f, 2026-09-07); **Cleaning ordered** while a chore is
pending. Three warning (`BadMinor`) items, rendered as red text with an exclamation icon: **Needs cleaning** when fouling is
past the threshold with no order pending (the cancelled-order case); **No pipe:
&lt;port&gt;**, one item per port, whenever that port cell has no pipe segment (the
same `HasConduit` test that stops the stream, so warning and behaviour agree).
Four fixed-text items rather than one with a list because the world hover card
shows status names only, so the name itself has to say which port (check c,
2026-09-07, found the single-item version uninformative there). The vanilla
`RequireInputs`/`RequireOutputs` components the def attaches are stripped from
the prefab with the consumer and dispenser: they covered stream A's ports only,
the input one went inert when the consumer was destroyed, and the output one
duplicated ours. Stripping must happen on the prefab, not per instance at spawn:
`Destroy` is deferred to end of frame, so a `RequireInputs` destroyed at spawn
still ran its own `OnSpawn` that frame and left "No Liquid Intake" and "Liquid
Pipe Empty" behind permanently (check c, second build). The prefab strip was
confirmed by log on 2026-09-08 and the spawn-time fallback removed. Note for any
future status cleanup: `RemoveStatusItem(StatusItem)` throws for items created
with `allow_multiples = true` (vanilla `NeedLiquidOut` is one); those need the
Guid from `AddStatusItem`; **Output near phase change** when an outlet leaves within 2 K of its
fluid's listed freezing or boiling point, held for 5 s after the last hit so it
does not flicker. The sim only changes an element's state 3 K beyond the listed
point and then rebounds 1.5 K toward it (Klei's stand-in for latent heat, and a
hysteresis band so a fluid sitting at the point does not flip state every tick),
so 2 K past the listed value is 5 K of real headroom. Verified 2026-09-07: water
left at 272.0 K, below its listed 273.15 K, and stayed liquid. The exchanger can push a fluid past a transition and the output pipe
then breaks under the vanilla rule; we warn rather than clamp, because clamping
would create heat from nothing.

## Shell heat, insulation, and melting
Designed 2026-09-07 from decompiled `AirConditioner`, `StructureTemperatureComponents`,
`BuildingTemplates`, `MonumentTopConfig`, `ThermalBlockConfig`, and
`StreamingAssets/elements/solid.yaml`. Code written, built, and verified 2026-09-07
(config recipe slot, `HeatExchangerCore` shell step and melt rule); see Calibration
and Verification plan below.

**Problem.** The fluids never touch the building body, so the exchanger emits no
heat to its room whatever it carries. A player could counterflow magma against
molten copper and the shell would sit at room temperature.

### Model
Heat reaches the room through two legs in series, and the plates can fail.

1. **Fluid to body** (ours). Each tick, each flowing packet trades heat with the
   body: `Q_i = G_shell/2 × (T_i − T_body) × dt`. The packet temperature moves by
   `Q_i / C_i`; the sum goes into the body through
   `GameComps.StructureTemperatures.ProduceEnergy(handle, kJ, source, dt)`. The
   argument is signed kilojoules per call (our joules ÷ 1000), the sim clamps the
   body to 0–10000 K, and a negative value is legal, so a hot body warms a cold
   packet and energy is conserved both ways. The source string
   `OPERATINGENERGY.PIPECONTENTS_TRANSFER` is the Aquatuner's. I expected reuse to
   put the rate in the vanilla energy tooltip for free; in game no such line
   appears on our info panel (see Verification plan). The sim effect is what
   matters and is verified through the body temperature; the display is cosmetic.
   Call with zero when both streams stall, as `AirConditioner` does, so any
   accumulated display rate resets.
2. **Body to room** (vanilla). The sim registers every building once via
   `AddBuildingHeatExchange(extents, primaryElement, MassForTemperatureModification,
   T, def.ThermalConductivity, operating_kw)`. The body's heat capacity is
   `0.2 × first-material mass × metal specific heat`; gaskets and insulation do
   not count. Conduction runs over all nine footprint cells using the metal's
   conductivity times the def multiplier. The formula is native code and cannot
   be read; it has to be measured.
3. **Melting**, two rules that coexist:
   - *Vanilla*: when the body exceeds the primary element's melting point the sim
     calls `StructureTemperatureComponents.DoMelt`, which spawns the metal's
     liquid (its `highTempTransitionTarget`) at the melting point in the
     building's origin cell, posts the "building melted" notification, and
     destroys the building. The mass spawned is the primary element's, which the
     game sets to the **sum of every construction slot** (verified: 800 kg
     copper + 200 kg insulator + 2×50 kg gaskets melted to 1100 kg). Gaskets and
     insulation therefore become metal, as they would for any vanilla
     multi-material building; accepted. Spawned exactly at the freezing point,
     the liquid solidifies almost at once into a natural tile in that cell.
   - *Ours*: when the **plate temperature** (the fluid mean already used as the
     fouling wall temperature) exceeds the metal's melting point, call `DoMelt`
     directly. It is public and static. This exists because insulation hides the
     plate temperature from the body: without it, ceramic-wrapped copper could
     carry magma forever. Insulated copper fed magma fails on plates; a copper
     exchanger in a magma-flooded room fails on body like any vanilla building.
     `DoMelt` skips elements whose transition target is Unobtanium, the yaml's
     null value for "no melt product". Every refined metal has one (Thermium:
     2950 K into molten niobium), so no exception is needed.

The rule tests the metal only. The insulator wraps the skin, not the plates, and
the skin sits near body temperature; every metal but thermium melts before
ceramic's 2123 K, and a thermium body that hot is left to vanilla. Fouling
continues to use the fluid mean as the plate temperature; the body is the outer
skin, not the plates.

### Third construction material: insulation
`G_shell = k_insulator × ShellFactor`, so the room loss follows the material the
player already knows from insulated pipes. Recipe tag **`Insulator`** from the
elements yaml. Five elements carry it:

| Material | k | Melts at | Notes |
|---|---|---|---|
| Refined Carbon | 3.1 | 4600 K | base game; cheap early rung, leaks most; Klei's yaml note models it on carbon-bonded carbon fiber (Mersen Calcarb), "not too good" by design; also our crude-oil fouling byproduct |
| Ceramic | 0.62 | 2123 K | base game; leaks a little |
| Insulite (`SuperInsulator`; named "Insulation" until U51-596100, Feb 29 2024) | 1e-5 | 3895 K | base game; leaks nothing |
| Rubber | 0.15 | 493 K | Aquatic DLC |
| Pearl | 0.9 | 1098 K | Aquatic DLC |

Igneous Rock lacks the tag (Klei draws the line at "building material, not
insulation"); Abyssalite (`Katairite`) is not buildable at all. There is no
uninsulated option on purpose: a heat exchanger has no reason to be one.

**Non-metal parts do not fail.** Gaskets and insulation survive whatever the
plates survive. Rubber melts at 493 K and Pearl at 1098 K, both reachable by the
body while the metal is sound, but modeling insulation failure would contradict
the already-accepted fiction that plastic gaskets survive the same temperatures,
and a player would see one fail beside the other. A gasket-free welded plate pack
with chemical cleaning is a different machine and a possible future design, not a
patch to this one.

Why the tag works: Insulation reaches insulated-pipe recipes only through
`BuildableRaw` in its yaml *tags list*, not its material category, so the
material picker resolves tags-list entries. Three materials are precedented by
the Monument Top (Glass, Diamond, Steel; specific elements). Verified in game
2026-09-07: the picker offers all five `Insulator` elements in `buildMenuSort`
order (Refined Carbon, Ceramic, Pearl, Rubber, Insulite), so it filters on the
recipe tag alone and not on `BuildableAny`. Refined metal stays first; the
first material is the primary element, which drives melting, body conductivity,
and body thermal mass. The core reads the chosen insulator from
`Deconstructable.constructionElements[2]`, the per-slot element list the game keeps
so deconstruction returns the right materials (verified 2026-09-07: spawn log
reads Ceramic and `SuperInsulator` correctly on two new buildings; a failed read,
as on a pre-existing two-material building, logs a warning and assumes Ceramic). Insulation mass is tier 3 (200 kg, `TIER3[0]`) against
800 kg of tier-5 metal: roughly an eight-cell perimeter at insulated-tile
density, and clearly the minor ingredient.

### Calibration
- `ShellFactor`: with Ceramic, target 1–3% of a copper exchanger's duty lost to
  the room, so `G_shell` near 1 kW/K against an 81 kW/K wall.
- `ShellFactor = 1500` in `HeatExchangerCore` stands (Ceramic 930 W/K,
  Refined Carbon 4650 W/K, Insulite 0.015 W/K).
- `def.ThermalConductivity` stays at the default. The risk was that the vanilla
  body-to-room leg would be the smaller resistance and hide the insulation choice.
  Measured 2026-09-07 on a thermium/Ceramic exchanger in 5 kg/tile oxygen with
  only 275 K water flowing: the body settled within about 0.3 K of its footprint
  air (bottom-center cell, which flickers 0.2 K as gas cells swap) while drawing
  7.4 kW from the room, so the leg is on the order of 25 kW/K or more, two
  orders above the Ceramic shell. Insulation is the limiter; the choice is
  visible. The leg scales with footprint gas mass, so even in 1 kg air it stays
  well above any non-Insulite shell. An earlier heating-side estimate (1.7 K
  offset at 16 kW, near 9 kW/K) disagrees by a factor of eight; the cold reading
  is the settled one and the earlier air readings may not have been footprint
  cells. Either way the conclusion holds.
- Body mass scale: keep the 0.2 default.

### Verification plan
- ~~Three-slot picker~~ verified: all five insulators offered (see above).
- ~~`[PCHX] insulator=` log line~~ verified: `insulator=Ceramic k=0.62 Gshell=930`
  and `insulator=SuperInsulator k=1E-05`.
- Body temperature tracks the fluid mean; the room warms behind Ceramic and not
  behind Insulation.
- ~~Magma through copper~~ verified 2026-09-07 (magma vs molten copper, Ceramic
  slot): melt logged at 1988 K against 1357 K on the first tick with fluid,
  notification posted, building gone, 1100 kg copper tile left in the origin cell.
  The plate rule fired through Ceramic wrapping, which is the case the body rule
  could never catch.
- ~~Tooltip shows the pipe-contents transfer rate~~ Checked: the info panel of a
  flowing exchanger shows no pipe-contents transfer heat line, and neither does
  an Aquatuner in the same save. So the source string is not surfaced in the
  current UI for vanilla either; nothing to fix on our side. Keeping the
  Aquatuner's string and the zero-at-stall call costs nothing and stays correct
  if Klei ever surfaces it. The body temperature proves the energy lands.

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
- **Research:** Liquid Tuning (tech id `LiquidTemperature`, verified in
  `Database.Techs`: it unlocks the Aquatuner, Liquid Tepidizer, Radiant Pipe,
  Conduction Panel and the liquid pipe sensors), one tier deeper than Improved
  Plumbing. Improved Plumbing (`ImprovedLiquidPiping`) was the
  first choice because the Plastic Gasket unlocks there, but a heat exchanger
  belongs with the other heat-moving equipment. A recipe ingredient does not
  gate a building on its own; only membership in a Tech's unlock list does.
- **Recipe:** refined metal (tier-5 mass) plus 2 Plastic Gaskets. The Steam
  Turbine uses the same pairing with 4 gaskets for a 5x3 footprint. Gaskets are
  thematic: cleaning means opening the plate pack. A third slot for an
  `Insulator` material is planned; see Shell heat, insulation, and melting.
- **Art:** borrowed `metalrefinery_kanim` until custom art exists.

## Source map
| File | Role |
|---|---|
| `Mod.cs` | Mod entry, string registration and localization patch, Db patch: plan-screen placement, research unlock, status-item and chore-type creation |
| `PCHXStrings.cs` | The `STRINGS` LocString tree: every player-visible string |
| `PlateCounterflowHeatExchangerConfig.cs` | BuildingDef, port offsets, three-material recipe, component wiring |
| `HeatExchangerCore.cs` | Both streams: plan / melt check / foul / exchange / shell / commit; secondary ports; fouling ledgers; status item |
| `Fouling.cs` | Fouling table, temperature factors, the per-tick deposition/removal step |
| `FoulingCleanWorkable.cs` | Cleaning errand: button, automatic trigger, work lifecycle, debris |
| `PCHXStatusItems.cs` | The five kinds of status item (eight objects: one per port for the pipe warning), their string callbacks, and the add/remove toggle helper |
| `PCHXChores.cs` | The Clean Plates chore type, built from `EmptyStorage`'s groups and priorities |
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
- Manual clean verified: Duplicant performs the errand, both pipes back up, one
  Salt chunk drops with exactly the ledger mass, fouling reads 0%, conductance
  returns to clean; series-resistance formula checked to four figures before and
  after.
- Pending order and its errand survive save, exit, and reload.
- Automatic trigger verified in full (2026-09-07): fires at exactly 50% (deposit
  0.3369 kg against a predicted 0.3367 kg crossing on thermium); a cancelled
  automatic order is not re-raised while fouling stays above 50%; a completed
  clean re-arms it and the next crossing fires again on its own.
- Shell heat measured in 5 kg/tile oxygen (2026-09-07). Cold-water-only run on
  the thermium/Ceramic exchanger: after twenty minutes the body read 290.7 K
  against bottom-center footprint oxygen at 17.2–17.5 °C (290.4–290.65 K,
  flickering as gas cells swap) while drawing 7.4 kW; the room had cooled from
  21.6 °C. Body and air track within a few tenths of a kelvin, so the vanilla
  leg is on the order of 25 kW/K or more and the insulation is the limiting
  resistance by two orders. The Insulite exchanger in the same save read body 293.8 K against
  footprint oxygen 20.7 °C (293.85 K) with zero shell exchange: adiabatic, as
  intended. The earlier heating-side estimate of 9 kW/K is superseded (see Shell
  heat, Calibration). Ceramic, Pearl, and Refined Carbon stay distinct in
  ordinary air. `ShellFactor = 1500` and default `def.ThermalConductivity` stand.
- Build menu (Utilities, Aquatuner group) and research gate (Liquid Tuning)
  verified; subcategory warning gone.
- Shell heat, insulation slot, and melt rule: builds clean and loads. Three-slot
  recipe renders; picker offers all five insulators. Shell step verified by hand
  on the first tick (Ceramic fallback on a two-material building, 930 W/K; signs
  and magnitudes of both packet terms match). Body settling point against room:
  measured, see the shell calibration bullet (body tracks footprint air within
  0.1 K; insulation is the limiter). Tooltip heat line checked: not shown
  (cosmetic, see Verification plan).
  ~~insulator read~~ verified. Melt on magma verified (see Shell heat,
  Verification plan).

- Polish batch written 2026-09-07; checks a, b, d, e, and f verified the same
  evening on the first build, c and the tooltip fit await the rework build. Items: rounded-percent trigger
  (`FoulingPercent`, `AutoCleanThresholdPercent`), Needs cleaning, Pipe not
  connected, Output near phase change. Checks: (a) auto order fires the same second
  the readout first shows 50% (verified); (b) cancel that order: the "Needs
  cleaning" warning appears, clears on a completed clean (verified; tooltip then
  cut to two short lines at the user's request, unbuilt); (c) deconstruct one port's pipe: a "No
  pipe: <port>" item names that port and clears when re-piped, with no vanilla
  "No liquid output" alongside (first version, 2026-09-07: the item appeared but
  the hover card did not name the port, and stream A's output also raised the
  vanilla item; second build: all four per-port names correct, but destroying
  `RequireInputs` at spawn left permanent vanilla "No Liquid Intake" and
  "Liquid Pipe Empty" items; components now stripped from the prefab, unbuilt.
  Third build: crash on load from the fallback scrub, because `NeedLiquidOut`
  allows multiple instances and can only be removed by Guid. Its log confirmed
  the prefab strip (consumer, RequireInputs, RequireOutputs removed; dispenser
  never present), so the whole fallback was deleted. Fourth build, 2026-09-08:
  loads clean, no vanilla intake or empty-pipe items; per-port items verified
  on the second build and unchanged since. Verified); (d) run 275 K water against
  a cold brine stream until the water outlet nears 273 K: "Output near phase
  change" names the stream and both temperatures, clears within 5 s of the
  outlet warming (verified with the 5 K margin: fired with water out at 272.0 K,
  cleared after brine was warmed to 344 K; margin then cut to 2 K; built, not yet re-verified at 2 K); (e) the errand shows as "Clean Plates" in the building's
  errand list and the duplicant's status reads "Cleaning heat exchanger plates",
  with no fallback warning in the log (verified); (f) after the strings refactor every text
  still renders (building name and description, all status items, both
  buttons, deposit list) with no raw `STRINGS.` keys showing, and every tooltip
  fits on screen (first build: the fouling tooltip overran both edges; lines
  shortened; second build: fits, deposit list visible, verified); (g) the log has no
  "Localization.Initialize not found" or "could not patch" warning.

## To do
Polish batch of 2026-09-07 fully verified as of 2026-09-08 (see Verification
record). Release decisions (2026-09-08): `DebugLog` stays on until the liquid
classification work is done and is flipped off as the last edit before a public
release; the "[PCHX] acceptance mismatch" warning is kept permanently (silent
unless the game's pipe acceptance rule changes); the PLib options menu is
deferred, a plain constant covers a fouling switch until someone asks.

**Player-facing**
- Mod options menu: a switch to disable fouling entirely, and a slider for
  `PackingFactor` (effectiveness). PLib's options system is the usual route and
  is usable for options alone even though we avoid it for conduits.
- Localization, stage 2: load our own `translations/<locale>.po` and generate a
  `.pot` template (see Localization for the signatures still needed). Stage 1,
  the `STRINGS` tree registered for translation, is built and verified (check f, 2026-09-07).
- Codex: the game's automatic Database entry exists (verified 2026-09-07: art,
  DESC, and recipe all shown), so DESC now carries a three-paragraph summary of
  the model (verified in game 2026-09-07). A custom section (diagram, fouling curve) would need the
  codex generator decompiled; low priority.
- Custom art (kanim tooling is Windows-centric; deferred).

**Model**
- Shell heat, insulation slot, and plate melt rule: built and verified; calibration
  closed with `ShellFactor = 1500` and default `def.ThermalConductivity`. The
  `G4` shell-log format fix is built and verified (Insulite prints 0.015 W/K).
- Liquid classification: written 2026-09-08 for all 52 yaml liquids (see Fouling
  model, Liquid classification), unbuilt. Compile risk: the 18 new `SimHashes`
  names. Verify in game: one new liquid per curve (Brackene for waxing, Ink for
  particulate) fouls at the modelled rate and drops the right chunk. The cleaning
  spawn already skips a missing element silently; a `LogWarning` there would be
  better (FoulingCleanWorkable, user's call). Decision (2026-09-07): the list of fouling fluids
  stays out of player-facing text. It would be unworkable in a tooltip once
  complete, and which fluids foul is left to player discovery; the description
  and tooltip name only the mechanisms (scaling, coking, biological growth).
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
  `DropAllWorkable`, `Toilet`, `ModUtil`, `PlanScreen.PlanInfo`, `AirConditioner`,
  `StructureTemperatureComponents`, `BuildingTemplates.CreateBuildingDef`,
  `MonumentTopConfig`, `ThermalBlockConfig`.
- Element data: `StreamingAssets/elements/solid.yaml` (tags, conductivities,
  melting points).
