# Thermal model
The heat-transfer model of the Plate Counterflow Heat Exchanger: the per-tick
ε-NTU exchange between the two streams, its single calibration knob, and the
shell-heat, insulation, and melt rules that couple the plates to the building
body and the room. The building and its ports are in [README.md](README.md); the
fouling resistances that add to the wall are in [FOULING.md](FOULING.md);
verification is recorded in [TESTING.md](TESTING.md).

## Contents
- [Counterflow ε-NTU](#counterflow-ε-ntu)
- [Calibration](#calibration)
- [Shell heat, insulation, and melting](#shell-heat-insulation-and-melting)
  - [Model](#model)
  - [Third construction material: insulation](#third-construction-material-insulation)
  - [Shell calibration](#shell-calibration)
- [References](#references)

## Counterflow ε-NTU
Counterflow ε-NTU per tick:

- `C = mass_kg × 1000 × specificHeatCapacity` (ONI specific heat is per gram)
- `NTU = G × dt / C_min`, `C_r = C_min / C_max`, `dt = 1.0` (conduit tick)
- `ε = (1 − e^(−NTU(1−C_r))) / (1 − C_r e^(−NTU(1−C_r)))`, or `NTU/(1+NTU)` when
  balanced
- `Q = ε × C_min × (T_hot − T_cold)`, applied equal and opposite

`G` is the fouled conductance. Fouling resistances add in series with the clean
wall: `1/G = 1/G_clean + R_A + R_B`, where `R_A` and `R_B` come from the two
fouling ledgers (FOULING.md, "Fouling model"). The player sees `1 − G/G_clean`.

The exchange runs only when both streams move; one stalled side turns the device
into a pipe for that tick (README.md, "Flow model"). There is no wall thermal
inertia, as with the thermal aquatuner.

## Calibration
Clean conductance `G_clean = k × 9 × PackingFactor`, where `k` is the construction
metal's thermal conductivity, 9 is the 3x3 footprint, and `PackingFactor` folds in
plate count and thickness. This is the only thermal calibration knob. The fouling
model has its own gameplay knob; see FOULING.md, "Calibration".

`PackingFactor = 150`. Calibration evidence, copper at full flow (10 kg/s brine
vs 10 kg/s water): NTU 2.38, ε 0.75, outlet temperatures matched a hand
calculation, and the brine outlet left hotter than the water outlet (a
successful counterflow temperature cross).

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

Because the displayed fouling percent is a conductance ratio, the same deposit
mass reads differently by metal: a thermium exchanger reads 50% at only 0.34 kg
of deposit while its effectiveness has barely moved, and lead needs 2.1 kg for
the same reading while its effectiveness is sensitive to every gram (FOULING.md,
"Deliberate choices", item 4).

## Shell heat, insulation, and melting
Designed 2026-09-07 from decompiled `AirConditioner`, `StructureTemperatureComponents`,
`BuildingTemplates`, `MonumentTopConfig`, `ThermalBlockConfig`, and
`StreamingAssets/elements/solid.yaml`. Implemented as the config's third recipe
slot and the `HeatExchangerCore` shell step and melt rule.

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
   appears on our info panel, and none appears on an Aquatuner in the same save,
   so the string is not surfaced in the current UI for vanilla either. Keeping
   the Aquatuner's string costs nothing and stays correct if Klei ever surfaces
   it. The sim effect is what matters and shows in the body temperature; the
   display is cosmetic. Call with zero when both streams stall, as
   `AirConditioner` does, so any accumulated display rate resets.
2. **Body to room** (vanilla). The sim registers every building once via
   `AddBuildingHeatExchange(extents, primaryElement, MassForTemperatureModification,
   T, def.ThermalConductivity, operating_kw)`. The body's heat capacity is
   `0.2 × first-material mass × metal specific heat`; gaskets and insulation do
   not count. Conduction runs over all nine footprint cells using the metal's
   conductivity times the def multiplier. The formula is native code and cannot
   be read; it has to be measured (see Shell calibration).
3. **Melting**, two rules that coexist:
   - *Vanilla*: when the body exceeds the primary element's melting point the sim
     calls `StructureTemperatureComponents.DoMelt`, which spawns the metal's
     liquid (its `highTempTransitionTarget`) at the melting point in the
     building's origin cell, posts the "building melted" notification, and
     destroys the building. The mass spawned is the primary element's, which the
     game sets to the **sum of every construction slot**: 800 kg copper + 200 kg
     insulator + 2×50 kg gaskets melt to 1100 kg. Gaskets and insulation
     therefore become metal, as they would for any vanilla multi-material
     building; accepted. Spawned exactly at the freezing point, the liquid
     solidifies almost at once into a natural tile in that cell.
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
the Monument Top (Glass, Diamond, Steel; specific elements). The picker offers
all five `Insulator` elements in `buildMenuSort` order (Refined Carbon, Ceramic,
Pearl, Rubber, Insulite), so it filters on the recipe tag alone and not on
`BuildableAny`. Refined metal stays first; the first material is the primary
element, which drives melting, body conductivity, and body thermal mass. The
core reads the chosen insulator from `Deconstructable.constructionElements[2]`,
the per-slot element list the game keeps so deconstruction returns the right
materials; a failed read, as on a pre-existing two-material building, logs a
warning and assumes Ceramic. Insulation mass is tier 3 (200 kg, `TIER3[0]`)
against 800 kg of tier-5 metal: roughly an eight-cell perimeter at
insulated-tile density, and clearly the minor ingredient.

### Shell calibration
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
  cells. Either way the conclusion holds. The Insulite exchanger in the same
  save sat at footprint air temperature with zero shell exchange: adiabatic, as
  intended. Ceramic, Pearl, and Refined Carbon stay distinct in ordinary air.
  Full readings are in TESTING.md, "Verification record".
- Body mass scale: keep the 0.2 default.

## References
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  (ε-NTU method).
- Refined metal thermal conductivities: oxygennotincluded.wiki.gg, Refined Metal.
- Vanilla reference classes read in decompile: `AirConditioner`,
  `StructureTemperatureComponents`, `BuildingTemplates.CreateBuildingDef`,
  `MonumentTopConfig`, `ThermalBlockConfig`.
- Element data: `StreamingAssets/elements/solid.yaml` (tags, conductivities,
  melting points).
