# Thermal model
This file contains the heat-transfer model of this Plate Counterflow Heat Exchanger mod. It includes the per-tick $\varepsilon$-NTU exchange between the two streams, the single calibration knob (packing factor), and the shell-heat, insulation, and melt rules that couple the plates to the building body and the room. The building and its ports are documented in [DEVELOPMENT.md](DEVELOPMENT.md). The fouling model that defines how deposits increase wall resistance is in [FOULING.md](FOULING.md). Testing and verification notes are recorded in [TESTING.md](TESTING.md).

## Contents
- [Counterflow ε-NTU](#counterflow-ε-ntu)
- [Calibration](#calibration)
- [Shell heat, insulation, and melting](#shell-heat-insulation-and-melting)
  - [Model](#model)
  - [Third construction material: insulation](#third-construction-material-insulation)
  - [Shell calibration](#shell-calibration)
- [References](#references)

## Counterflow ε-NTU
Heat flows from the hotter stream to the colder one through the metal plates that
separate them. A bigger, better-conducting plate pack moves more heat, and so
does a slower flow, because each kilogram of liquid then spends longer against
the plates. Only so much heat could possibly move; effectiveness, written
$\varepsilon$, is the fraction of that possible heat the exchanger actually
moves. Counterflow means the two streams run in opposite directions, which keeps
a useful temperature difference along the whole length of the pack; that is why
the cold stream can leave hotter than the hot stream leaves. Whatever heat the
hot stream loses, the cold stream gains, exactly.

$G$ is the plate pack's conductance: the heat in watts that crosses the plates
for each kelvin of temperature difference between the streams. The construction
metal and the geometry set it (see "Calibration"). $T_\mathrm{hot}$ and
$T_\mathrm{cold}$ are the two inlet temperatures, the temperatures of the liquid
arriving in each pipe. $\mathrm{NTU}$, the number of transfer units, compares
that conductance against how much heat capacity the flow carries past it each
tick; a large $\mathrm{NTU}$ means plenty of plate for the flow. $C_r$ is the
ratio of the smaller of the two heat capacity rates to the larger, so it is 1
when the streams are matched.

Counterflow $\varepsilon$-NTU per tick, for a packet of mass $m$ in kilograms with specific
heat $c_p$ (specific heat in ONI is per gram), heat capacity rate $C$ (the joules
needed to warm one tick's worth of that stream by one kelvin; $C_{\min}$
and $C_{\max}$ are the smaller and larger of the two streams), conduit tick
$\Delta t = 1.0$ in seconds, and heat $Q$ in joules:

$$C = m \times 1000 \times c_p$$

$$\mathrm{NTU} = \frac{G\,\Delta t}{C_{\min}}, \qquad C_r = \frac{C_{\min}}{C_{\max}}$$

$$\varepsilon = \frac{1 - e^{-\mathrm{NTU}(1 - C_r)}}{1 - C_r\,e^{-\mathrm{NTU}(1 - C_r)}}$$

$\varepsilon = \mathrm{NTU} / (1 + \mathrm{NTU})$ when the streams are balanced.

$$Q = \varepsilon \, C_{\min} \, (T_\mathrm{hot} - T_\mathrm{cold})$$

The hot packet loses exactly $Q$ and the cold packet gains exactly $Q$, so no
heat appears or disappears in the exchange.

$G$ is the fouled conductance, the conductance of the pack with whatever deposit
is on it. Fouling resistances add in series with the clean
wall: $1/G = 1/G_\mathrm{clean} + R_A + R_B$, where $G_\mathrm{clean}$ is the
conductance of the same pack with no deposit on it, and $R_A$ and $R_B$ are the
thermal resistances in kelvin per watt of the deposit on each side; both come from the two
fouling ledgers (FOULING.md, "Model"). The player sees $1 - G/G_\mathrm{clean}$.

Heat exchange occurs only when both streams are moving. Any stalled side turns the device
into a pipe for that tick (DEVELOPMENT.md, "Flow model"). There is no wall thermal
inertia, as with the thermal aquatuner building.

## Calibration
Clean conductance $G_\mathrm{clean} = k \times 9 \times$ `PackingFactor`, where $k$ is the construction
metal's thermal conductivity, 9 is the building's 3x3 footprint, and `PackingFactor` models the plate count and thickness. This is the only thermal calibration knob. The fouling model has its own gameplay knob (FOULING.md, "Calibration").

The chosen `PackingFactor` value of 150 was selected by experiment and validation. An exchanger made of copper was run at full flow (10 kg/s brine vs 10 kg/s water). Observed values ($\mathrm{NTU}$ 2.38, $\varepsilon$ 0.75) and outlet temperatures matched a hand calculation, and the brine outlet left hotter than the water outlet, demonstrating successful counterflow temperature cross.

Predicted balanced water/water $\varepsilon$ at 10 kg/s:

| Metal | $k$ | $\varepsilon$ |
|---|---|---|
| Thermium | 220 | 0.88 |
| Aluminum | 205 | 0.87 |
| Copper, Gold, Tungsten | 60 | 0.66 |
| Iron, Steel | 55 / 54 | 0.64 |
| Lead | 35 | 0.53 |

Effectiveness increases at lower flow rates. At 1 kg/s, every metal in the game is above 0.92. Dropping the packing factor to 100 stretches the spread to roughly
Aluminum 0.82 / Lead 0.43 if a wider gap is ever desired.

Thermal effectiveness $\varepsilon$ flattens out and approaches a fixed upper limit when adding more heat transfer surface area (diminishing returns), so high- $k$ metals cluster near 100% effectiveness. Material selection is most important at full throughput. At high $\mathrm{NTU}$ the plate wall stops being the limiting resistance. Allowed construction material is restricted to refined metals. Ore type metals were considered as possible building material candidates, but rejected due to low thermal conductivity.
For example, gold amalgam ($k \approx 2$) gave $\varepsilon \approx 0.06$, resulting in a useless building. In-game, a conduction panel built from gold amalgam is similarly useless, but still allowed.

Because the displayed fouling percent is a conductance ratio, the same deposit
mass reads differently per metal. For example, a thermium exchanger reads 50% at
only 0.34 kg of deposit while its effectiveness has barely dropped. By contrast, lead needs 2.1 kg for the same reading even though its effectiveness is sensitive to every gram (FOULING.md, "Deliberate choices", item 4).

## Shell heat, insulation, and melting
The plates sit inside a building body, and the body sits in a room. The body
trades heat with the liquid through an insulating wrap, and with the room through
its nine footprint cells. The insulator chosen in the third recipe slot decides
how leaky that wrap is; refined carbon leaks the most and insulite almost
nothing. So a hot exchanger warms its room a little and a cold room cools the
liquid a little, and the player pays for a tight shell. Separately, plates hotter
than the construction metal's melting point destroy the building, wrap or no
wrap.

Modeled after decompiled `AirConditioner`, `StructureTemperatureComponents`,
`BuildingTemplates`, `MonumentTopConfig`, `ThermalBlockConfig`, and
`StreamingAssets/elements/solid.yaml`.

**Problem.** The fluids never touch the building body, so without a shell heat model,
the exchanger emits no heat to its room no matter what materials are flowing through
it. A player could counterflow magma against molten copper and the shell would sit at room temperature.

### Model
Heat reaches the room through two legs in series. The plates can melt as described here.

Below, $G_\mathrm{shell}$ is the conductance of the insulating wrap between
liquid and body in watts per kelvin, set by the insulator (see "Third
construction material: insulation"). $T_\mathrm{body}$ is the temperature of the
building body. $T_i$ and $C_i$ are the temperature and heat capacity rate of the
packet on stream $i$, and $Q_i$ is the heat that packet trades with the body in
one tick.

1. **Fluid to body** (implemented by this mod). Each tick, each flowing packet trades
   heat with the body: $Q_i = \tfrac{1}{2} G_\mathrm{shell} \, (T_i - T_\mathrm{body}) \, \Delta t$. The packet temperature moves by $Q_i / C_i$. The sum goes into the body through
   `GameComps.StructureTemperatures.ProduceEnergy(handle, kJ, source, dt)`. The
   argument is signed kilojoules per call (our joules $\div 1000$), the sim clamps the
   body to 0–10000 K. Negative values are allowed, so a hot body warms a cold
   packet and energy is conserved both ways.
2. **Body to room** (implemented by the game). The sim registers every building once via
   `AddBuildingHeatExchange(extents, primaryElement, MassForTemperatureModification,
   T, def.ThermalConductivity, operating_kw)`. The body's heat capacity is
   $0.2 \times$ first-material mass $\times$ metal specific heat, and the rest of the building recipe (gaskets and insulation) do not count. Conduction runs over all nine footprint cells using the metal's conductivity times the def multiplier (see Shell calibration).
3. **Melting** (two rules)
   - *Vanilla*: when the body exceeds the primary element's melting point, the sim
     calls `StructureTemperatureComponents.DoMelt`, which spawns the metal's
     liquid (its `highTempTransitionTarget`) at the melting point in the
     building's origin cell, posts the "building melted" notification, and
     destroys the building. The mass spawned is the primary element's, which the
     game sets to the **sum of every construction slot**: 800 kg copper + 200 kg
     insulator + $2 \times 50$ kg gaskets melt to 1100 kg refined metal. Gaskets and insulation
     therefore become refined metal, as they would for any vanilla multi-material
     building.
   - *This Mod*: when the **plate temperature** (the fluid mean already used as the
     fouling wall temperature) exceeds the metal's melting point, call `DoMelt`
     directly. This exists because insulation hides the plate temperature from the
     body. Without it, a ceramic-wrapped copper heat exchanger could carry magma forever. An insulated copper heat exchanger fed magma melts due to this rule (i.e. plate failure). A copper heat exchanger in a magma-flooded room fails on the body according to the vanilla rule like any other building.


This mod's melting rule tests the metal only. The insulator wraps the skin, not the plates, and the skin sits near body temperature. Every metal except thermium melts before
ceramic's 2123 K, and a thermium body that hot is left to the vanilla melting rule. Fouling always uses the fluid mean as the plate temperature (the body is the outer
skin, not the plates).

### Third construction material: insulation
$G_\mathrm{shell} = k_\mathrm{insulator} \times$ `ShellFactor`, where
$k_\mathrm{insulator}$ is the chosen insulator's thermal conductivity and
`ShellFactor` stands in for the wrap's area and thickness. The room loss therefore follows the behavior the
player already knows from insulated pipes. All five elements that carry the recipe tag **`Insulator`** from solid.yaml are allowed by the recipe:

| Material | $k$ | Melts at | Notes |
|---|---|---|---|
| Refined Carbon | 3.1 | 4600 K | base game, cheap early rung, leaks most |
| Ceramic | 0.62 | 2123 K | base game, leaks a little |
| Insulite | 1e-5 | 3895 K | base game, leaks nothing |
| Rubber | 0.15 | 493 K | Aquatic DLC |
| Pearl | 0.9 | 1098 K | Aquatic DLC |

Klei left a note in solid.yaml that the insulative property of refined carbon is modeled on carbon-bonded carbon fiber (Mersen Calcarb), and "not too good" by design. 

There is no uninsulated option by design because such a heat exchanger would suffer significant thermal loss.

**Non-metal parts do not fail.** Gaskets and insulation survive whatever the
plates survive. Rubber melts at 493 K and Pearl at 1098 K, both reachable by the
body while the metal is unmelted. Modeling insulation failure would contradict
the convenient fiction that plastic gaskets survive the same temperatures. Gasket-free welded plate packs exist in the real world, but implementing one (with chemical cleaning) is a different building.

Insulation mass was selected to be tier 3 (200 kg, `TIER3[0]`) against 800 kg of tier-5 metal.

### Shell calibration
- `ShellFactor`: with Ceramic, target 1–3% of a copper exchanger's duty lost to
  the room, so $G_\mathrm{shell}$ near 1 kW/K against an 81 kW/K wall.
- `ShellFactor` $= 1500$ in `HeatExchangerCore` (Ceramic 930 W/K,
  Refined Carbon 4650 W/K, Insulite 0.015 W/K).
- `def.ThermalConductivity` stays at the game default. The design risk was that the 
  vanilla body-to-room leg would be the smaller resistance and hide the insulation choice.
  Measured experimentally on a thermium/Ceramic exchanger in 5 kg/tile oxygen, with 275 K water flowing. The heat exchanger body settled within about 0.3 K of its footprint
  air (tested at bottom-center cell, which flickered ~0.2 K as gas cells swapped in the simulation) while drawing 7.4 kW from the room. This indicates the body-to-room leg is on the order of 25 kW/K or more, two orders of magnitude above the Ceramic shell. The leg scales with footprint gas mass, so even in 1 kg air it stays well above any non-Insulite shell. An thermium/insulite exchanger tested in the same save sat at footprint air temperature with zero shell exchange. From a gameplay perspective, all possible insulation choices are distinct meaningful in ordinary air.
  Full readings are in TESTING.md, "Verification record".
- Body mass scale stays at the game default (0.2).

## References
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  ($\varepsilon$-NTU method).
- Refined metal thermal conductivities: oxygennotincluded.wiki.gg, Refined Metal.
- Vanilla reference classes read in decompile: `AirConditioner`,
  `StructureTemperatureComponents`, `BuildingTemplates.CreateBuildingDef`,
  `MonumentTopConfig`, `ThermalBlockConfig`.
- Element data: `StreamingAssets/elements/solid.yaml` (tags, conductivities,
  melting points).
