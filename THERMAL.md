# Thermal model
This document describes the heat-transfer model of the Plate Counterflow Heat Exchanger. It includes the per-tick
$\varepsilon$-NTU exchange between the two streams, the one calibration knob for that exchange, `PackingFactor`, and the shell-heat, insulation and melt rules that couple the plates to the building body and the room. The overall mod description is [README.md](README.md), and the fouling model that increases wall resistance is in [FOULING.md](FOULING.md).

## Contents
- [Counterflow ε-NTU](#counterflow-ε-ntu)
- [Calibration](#calibration)
- [Shell heat, insulation, and melting](#shell-heat-insulation-and-melting)
  - [Model](#model)
  - [Third construction material: insulation](#third-construction-material-insulation)
  - [Shell calibration](#shell-calibration)
- [References](#references)

## Counterflow ε-NTU
Heat flows from the hotter stream to the colder one through the metal plates that separate them. A bigger, better-conducting plate pack moves more heat. So does a slower flow, because each kilogram of liquid spends longer against the plates. Effectiveness, written
$\varepsilon$, is the fraction of the maximum possible heat that the exchanger actually moves. Counterflow means the two streams run in opposite directions, which keeps a useful temperature difference along the whole length of the pack. This geometry is what enables
the cold stream to leave hotter than the hot stream leaves. In other words, the cold stream exactly gains the heat that the hot stream loses.

$G$ is the plate pack's conductance, the heat in watts that crosses the plates for each kelvin of temperature difference between the streams. The construction metal and the plate packing factor determine $G$ (see "Calibration"). The two inlet temperatures, $T_\mathrm{hot}$ and
$T_\mathrm{cold}$, represent the temperatures of the liquid arriving in each pipe. $\mathrm{NTU}$, the number of transfer units, compares
$G$ against how much heat capacity the flow carries past it each
tick. A large $\mathrm{NTU}$ indicates high effectiveness, meaning the fluid temperatures leaving the heat exchanger approach their maximum possible temperature difference. $C_r$ is the ratio of the heat capacity rates between the two flows, with the smaller in the numerator the larger in the denominator. $C_r$ is equal to 1 when the streams are matched.

Counterflow $\varepsilon$-NTU per tick is given by the following formula, where:

* $m$ is the mass of the packet in kilograms
* $c_p$ is the specific heat of the packet (specific heat in ONI is per gram)
* $C$ is the heat capacity rate of the packet (the joules needed to warm one tick's worth of that stream by one kelvin, where $C_{\min}$ and $C_{\max}$ are the smaller and larger of the two streams)
* $\Delta t$ is the conduit tick rate (1.0 seconds)
* and $Q$ is the heat in joules

$$C = m \times 1000 \times c_p$$

$$\mathrm{NTU} = \frac{G\,\Delta t}{C_{\min}}, \qquad C_r = \frac{C_{\min}}{C_{\max}}$$

$$\varepsilon = \frac{1 - e^{-\mathrm{NTU}(1 - C_r)}}{1 - C_r\,e^{-\mathrm{NTU}(1 - C_r)}}$$

$\varepsilon = \mathrm{NTU} / (1 + \mathrm{NTU})$ when the streams are balanced (i.e. $C_r$ is equal to 1).

$$Q = \varepsilon \, C_{\min} \, (T_\mathrm{hot} - T_\mathrm{cold})$$

The cold packet gains exactly $Q$ and the hot packet loses exactly $Q$, so no heat appears or disappears in the exchange.

Fouling raises the resistance of the plates, so $G$ falls as deposit builds up. The formula, the two fouling ledgers, and the status readout are in [FOULING.md](FOULING.md), "Model".

Heat exchange occurs only when both streams are moving. When one side stalls, the flowing side passes through like a pipe for that tick; it still fouls and still trades shell heat with the body ([README.md](README.md), "What it is"). The plates carry no thermal inertia, as with the Aquatuner.

## Calibration
Clean conductance $G_\mathrm{clean} = k \times 9 \times$ `PackingFactor`, where $k$ is the construction metal's thermal conductivity, 9 is the building's 3x3 footprint, and `PackingFactor` (150) represents the plate count and thickness. `PackingFactor` is the only calibration knob in the exchange model. `ShellFactor` sets how strongly the body couples to the room and is described under "Shell heat, insulation, and melting". The fouling model has its own knob ([FOULING.md](FOULING.md), "Calibration").

Predicted balanced water/water $\varepsilon$ at 10 kg/s:

| Metal | $k$ | $\varepsilon$ |
|---|---|---|
| Thermium | 220 | 0.88 |
| Aluminum | 205 | 0.87 |
| Copper, Gold, Tungsten | 60 | 0.66 |
| Iron, Steel | 55 / 54 | 0.64 |
| Lead | 35 | 0.53 |

Effectiveness rises as flow rate decreases. At 1 kg/s every metal in the game reaches about 0.92 or better.

Effectiveness $\varepsilon$ flattens toward an upper limit as heat transfer area grows, so high-$k$ metals cluster near the top of the scale. The metal therefore matters most at full throughput, because at high $\mathrm{NTU}$ the plate wall is no longer the limiting resistance.

Because the displayed fouling percent is the deposit's share of the total resistance, the same deposit mass impacts each metal differently. A thermium exchanger reads 50% at only 0.34 kg of deposit while its effectiveness has barely dropped. At the same time, lead needs 2.1 kg for the same fouling percent even though its effectiveness is sensitive to every gram ([FOULING.md](FOULING.md), "Deliberate choices", item 4).

## Shell heat, insulation, and melting
The plates sit inside a building body, and the body sits in an environment. The body trades heat with the liquid through an insulating wrap, and with the environment through its nine footprint cells. The insulator chosen in the third recipe slot determines how much heat escapes the wrap. Of the allowed options, refined carbon leaks the most and insulite almost nothing. Overall, a hot exchanger warms its room a little and a cold room cools the liquid a passing through the heat exchanger a little. Fluids that heat the plates beyond the construction metal's melting point will destroy the building, regardless of the insulation material chosen.

### Model
Heat reaches the room through two legs in series.

*  $G_\mathrm{shell}$ is the conductance of the insulating wrap in watts per kelvin
* $T_\mathrm{body}$ is the temperature of the building body
* $T_i$ and $C_i$ are the temperature, heat capacity rate of the packet on stream $i$
* $\Delta t$ is the conduit tick rate (1.0 seconds)
* and $Q_i$ is the heat that packet trades with the body per tick

1. **Fluid to body** (this mod). Each tick, each flowing packet trades heat with the body: $Q_i = \tfrac{1}{2} G_\mathrm{shell} \, (T_i - T_\mathrm{body}) \, \Delta t$. The packet temperature moves by $Q_i / C_i$. The sum goes into the body as signed energy, so a hot body warms a cold packet and energy is conserved both ways.

2. **Body to room** (game rule). The sim conducts from the body over all nine footprint cells using the metal's conductivity times 0.2, a constant defined by the game that impacts the apparent mass of the building. The total heat capacity is determined by the metal alone, the mass of gaskets and insulation do not count.

3. **Melting**, by two rules:
   - *Game Rule*: when the body exceeds the metal's melting point, the sim melts the building and spawns the metal's liquid in the origin cell. The mass spawned is the sum of all construction slots, so gaskets and insulation "melt" into refined metal.
   - *This mod*: when the **plate temperature** (the fluid mean, the same value the fouling model uses as the wall temperature) exceeds the metal's melting point, the plates fail and the building melts. Insulation hides the plate temperature from the body, so without this rule a ceramic-wrapped copper exchanger could carry magma forever. A copper exchanger standing in a magma-flooded room still fails on the body, by the base game rule.

This mod's melting rule tests the metal only. The insulator wraps the body, not the plates. Most metals melt below ceramic's 2123 K, so the metal test fires first. Steel, niobium, thermium and tungsten melt hotter than ceramic; a body that hot on those metals is left to the base game rule.

### Third construction material: insulation
$G_\mathrm{shell} = k_\mathrm{insulator} \times$ `ShellFactor`, where
$k_\mathrm{insulator}$ is the chosen insulator's thermal conductivity and `ShellFactor` represents the wrap's area and thickness. Room loss therefore follows the same behavior as insulated pipes. The recipe
accepts all five elements that carry the `Insulator` tag:

| Material | $k$ | Melts at | Notes |
|---|---|---|---|
| Refined Carbon | 3.1 | 4600 K | cheap early game, leaks most |
| Ceramic | 0.62 | 2123 K | leaks a little |
| Insulite | 1e-5 | 3895 K | leaks nothing |
| Rubber | 0.15 | 493 K | Aquatic Planet Pack DLC |
| Pearl | 0.9 | 1098 K | Aquatic Planet Pack DLC |

There is no uninsulated option. An unwrapped exchanger would lose too much heat to the environment, like a radiant pipe.

**Non-metal parts do not fail.** Gaskets and insulation survive whatever the plates survive, even though rubber melts at 493 K and pearl at 1098 K. This failure mode is intentionally not modeled, even though both temperatures are reachable by the body while the metal is intact.

### Shell calibration
Heat exchanger duty is the total amount of thermal energy transferred per tick from the hot stream to the cold stream. At our chosen `ShellFactor` $= 1500$, ceramic gives $G_\mathrm{shell} = 930$ W/K, refined carbon 4650 W/K, and insulite 0.015 W/K. Against a copper exchanger's 81 kW/K wall, ceramic loses 1–3% of the duty to the environment.

Construction metal thermal conductivity and the 0.2 body mass scale remain at the game defaults. The body-to-room leg is two orders of magnitude greater than a ceramic shell, so insulator material selection is impactful from a gameplay perspective. Insulite is effectively adiabatic. The body-to-room leg scales with the gas mass in the building footprint, so even in thin atmosphere it stays well above any non-insulite shell.

## References
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  ($\varepsilon$-NTU method).
- Refined metal thermal conductivities: oxygennotincluded.wiki.gg, Refined Metal.
- Element data: `StreamingAssets/elements/solid.yaml` (tags, conductivities,
  melting points).