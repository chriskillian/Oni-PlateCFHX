# Thermal model
The heat-transfer model of the Plate Counterflow Heat Exchanger: the per-tick
$\varepsilon$-NTU exchange between the two streams, the single thermal
calibration knob, and the shell-heat, insulation and melt rules that couple the
plates to the building body and the room. The building and its ports are in
[DEVELOPMENT.md](DEVELOPMENT.md). The fouling model that raises wall resistance
is in [FOULING.md](FOULING.md).

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

Heat exchange occurs only when both streams are moving. Any stalled side turns
the device into a pipe for that tick (DEVELOPMENT.md, "Flow model"). The plates
carry no thermal inertia, as with the Aquatuner.

## Calibration
Clean conductance $G_\mathrm{clean} = k \times 9 \times$ `PackingFactor`, where
$k$ is the construction metal's thermal conductivity, 9 is the building's 3x3
footprint, and `PackingFactor` (150) stands for the plate count and thickness. It
is the only thermal calibration knob; the fouling model has its own
(FOULING.md, "Calibration").

Predicted balanced water/water $\varepsilon$ at 10 kg/s:

| Metal | $k$ | $\varepsilon$ |
|---|---|---|
| Thermium | 220 | 0.88 |
| Aluminum | 205 | 0.87 |
| Copper, Gold, Tungsten | 60 | 0.66 |
| Iron, Steel | 55 / 54 | 0.64 |
| Lead | 35 | 0.53 |

Effectiveness rises as flow falls. At 1 kg/s every metal in the game is above
0.92.

$\varepsilon$ flattens toward an upper limit as heat transfer area grows, so
high-$k$ metals cluster near the top of the scale. The metal therefore matters
most at full throughput; at high $\mathrm{NTU}$ the plate wall is no longer the
limiting resistance. Only refined metals are allowed, because ore-grade metals
conduct far too poorly to make a working exchanger.

Because the displayed fouling percent is a conductance ratio, the same deposit
mass reads differently per metal. A thermium exchanger reads 50% at only 0.34 kg
of deposit while its effectiveness has barely dropped; lead needs 2.1 kg for the
same reading even though its effectiveness is sensitive to every gram
(FOULING.md, "Deliberate choices", item 4).

## Shell heat, insulation, and melting
The plates sit inside a building body, and the body sits in a room. The body
trades heat with the liquid through an insulating wrap, and with the room through
its nine footprint cells. The insulator chosen in the third recipe slot decides
how leaky that wrap is; refined carbon leaks the most and insulite almost
nothing. So a hot exchanger warms its room a little and a cold room cools the
liquid a little, and the player pays for a tight shell. Separately, plates hotter
than the construction metal's melting point destroy the building, wrap or no
wrap.

The engine mechanics this section rests on are in ENGINE.md, "Buildings, sim
heat, plan screen, research".

### Model
Heat reaches the room through two legs in series.

Below, $G_\mathrm{shell}$ is the conductance of the insulating wrap between
liquid and body in watts per kelvin, set by the insulator (see "Third
construction material: insulation"). $T_\mathrm{body}$ is the temperature of the
building body. $T_i$ and $C_i$ are the temperature and heat capacity rate of the
packet on stream $i$, and $Q_i$ is the heat that packet trades with the body in
one tick.

1. **Fluid to body** (this mod). Each tick, each flowing packet trades heat with
   the body: $Q_i = \tfrac{1}{2} G_\mathrm{shell} \, (T_i - T_\mathrm{body}) \, \Delta t$.
   The packet temperature moves by $Q_i / C_i$. The sum goes into the body as
   signed energy, so a hot body warms a cold packet and energy is conserved both
   ways.
2. **Body to room** (the game). The sim conducts from the body over all nine
   footprint cells using the metal's conductivity times the def multiplier (see
   "Shell calibration"), and gives the body a heat capacity set by the metal
   alone; gaskets and insulation do not count.
3. **Melting**, by two rules:
   - *Vanilla*: when the body exceeds the metal's melting point, the sim melts the
     building and spawns the metal's liquid in the origin cell. The mass spawned is
     the sum of every construction slot, so gaskets and insulation come back as
     refined metal.
   - *This mod*: when the **plate temperature** (the fluid mean, the same value the
     fouling model uses as the wall temperature) exceeds the metal's melting point,
     the plates fail and the building melts. Insulation hides the plate temperature
     from the body, so without this rule a ceramic-wrapped copper exchanger could
     carry magma forever. A copper exchanger standing in a magma-flooded room still
     fails on the body, by the vanilla rule.

The plate rule tests the metal only. The insulator wraps the skin, not the plates,
and the skin sits near body temperature. Every metal except thermium melts below
ceramic's 2123 K, and a thermium body that hot is left to the vanilla rule.

### Third construction material: insulation
$G_\mathrm{shell} = k_\mathrm{insulator} \times$ `ShellFactor`, where
$k_\mathrm{insulator}$ is the chosen insulator's thermal conductivity and
`ShellFactor` stands in for the wrap's area and thickness. Room loss therefore
follows the behaviour a player already knows from insulated pipes. The recipe
accepts all five elements that carry the `Insulator` tag:

| Material | $k$ | Melts at | Notes |
|---|---|---|---|
| Refined Carbon | 3.1 | 4600 K | base game, cheap early rung, leaks most |
| Ceramic | 0.62 | 2123 K | base game, leaks a little |
| Insulite | 1e-5 | 3895 K | base game, leaks nothing |
| Rubber | 0.15 | 493 K | Aquatic DLC |
| Pearl | 0.9 | 1098 K | Aquatic DLC |

There is no uninsulated option; an unwrapped exchanger would bleed too much of
its duty into the room to be worth building.

**Non-metal parts do not fail.** Gaskets and insulation survive whatever the
plates survive, even though Rubber melts at 493 K and Pearl at 1098 K, both
reachable by the body while the metal is intact.

### Shell calibration
- `ShellFactor` $= 1500$: Ceramic gives $G_\mathrm{shell} = 930$ W/K, Refined
  Carbon 4650 W/K, Insulite 0.015 W/K. Against a copper exchanger's 81 kW/K wall,
  Ceramic loses 1–3% of the duty to the room.
- `def.ThermalConductivity` and the body mass scale stay at the game defaults. The
  body-to-room leg is two orders of magnitude stiffer than a Ceramic shell, so the
  insulator choice is what the player feels; Insulite is effectively adiabatic. The
  leg scales with footprint gas mass, so even in thin air it stays well above any
  non-Insulite shell.

## References
- Incropera and DeWitt, *Fundamentals of Heat and Mass Transfer*, ch. 11
  ($\varepsilon$-NTU method).
- Refined metal thermal conductivities: oxygennotincluded.wiki.gg, Refined Metal.
- Element data: `StreamingAssets/elements/solid.yaml` (tags, conductivities,
  melting points).
