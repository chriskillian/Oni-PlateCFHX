# Fouling model
This document describes the fouling model of the Plate Counterflow Heat Exchanger. It includes the asymptotic deposition and removal step, its one gameplay knob, the temperature-factor curves, the classification of liquids, and what a completed cleaning errand does to the fouling ledgers. The overall mod description is [README.md](README.md), and the thermal model that describes the per-tick $\varepsilon$-NTU exchange between the two streams is in [THERMAL.md](THERMAL.md).

## Contents
- [Model](#model)
- [Calibration](#calibration)
- [Fluids and byproducts](#fluids-and-byproducts)
- [Liquid classification](#liquid-classification)
- [Pacing](#pacing)
- [Deliberate choices](#deliberate-choices)
- [Cleaning mechanics](#cleaning-mechanics)
- [References](#references)

## Model
Solids carried in a liquid settle on the plates and slow heat transfer, the same way scale builds up in a kettle. How quickly they settle depends on the liquid, the temperature of the plate wall, and the flow rate. Low flow rates allow more deposits, but high flow rates also scour deposits from the plates. Scouring grows faster with flow than settling does, so the deposits climb toward a ceiling instead of growing forever. The cleaning errand removes deposits and returns the accumulated mass back to the player.

The deposition model is asymptotic, after Kern and Seaton. Deposition grows with throughput, while shear removal grows with throughput squared. Thus the overall deposit ceiling is lower at high flow rates.

Fouling accumulation is given by the following formula, per tick, per stream:
* $m$ is the packet mass in kilograms (a full pipe carries 10 kg per tick)
* $T_\mathrm{wall}$ is the wall temperature (the temperature of the plate surface the deposit forms on)
* $f(T_\mathrm{wall})$ is the temperature factor, a multiplier that scales deposition up or down (see "Fluids and byproducts")
* $D$ is the deposit already on the wall, in kg
* $\Delta t$ is the tick interval (1.0 seconds)
* $\text{rate}$ is the per-fluid deposition rate in kg of deposit per kg of fluid
* and $\tau$ is the removal time constant in seconds (the time scale over which full flow scours deposit away)

$$\text{deposition} = \text{rate} \times f(T_\mathrm{wall}) \times m$$

$$\text{removal} = \left(\frac{m}{10\ \mathrm{kg}}\right)^{2} \times D \times \frac{\Delta t}{\tau}$$

$T_\mathrm{wall}$ is calculated as the mean of the two inlet temperatures, or the single flowing one in the case that one flow is stopped.

State is maintained as a **mass ledger** per stream, in kilograms of deposit per byproduct element. State is saved with the building and preserved across game loads. Thermal resistance is derived from the deposit mass, where each kilogram of deposit adds $10^{-5}$ K/W. This ensures that mass is the single source of truth for fouling deposits and cleaning can hand back exactly what was deposited.

The deposit adds thermal resistance in series with the clean plate pack:

$$R = R_\mathrm{clean} + R_A + R_B$$

$R_\mathrm{clean}$ is the resistance of the plates with no deposit, and $R_A$ and $R_B$ are the deposit resistances on each side, all in kelvin per watt. The thermal model works with the plate conductance $G = 1/R$ ([THERMAL.md](THERMAL.md), "Counterflow ε-NTU"). The building status shows the deposit's share of the total resistance, $(R_A + R_B)/R$, which equals $1 - G/G_\mathrm{clean}$. A metal with a small $R_\mathrm{clean}$ therefore reads a high percentage from a small deposit.

## Calibration
The fouling model has one gameplay knob, the removal time constant $\tau$ (`Fouling.RemovalTimeConstant`, currently 600 s). $\tau$ sets how fast the deposit approaches its asymptote, and therefore how many cycles pass before a cleaning errand is triggered. It also scales the asymptote, so it is tuned together with the deposition rates (see below).

| Constant | Value | Role |
|---|---|---|
| `DepositionRate` (per fluid) | table below | kg of deposit per kg of fluid, before $f(T_\mathrm{wall})$ |
| `ResistancePerKg` | 1e-5 K/W per kg | converts ledger mass to thermal resistance |
| `ReferenceMassPerTick` | 10 kg | full-pipe flow; shear removal scales with $(m / 10\ \mathrm{kg})^{2}$ |

Setting deposition equal to removal gives the asymptotic deposit
$\text{rate} \times f(T_\mathrm{wall}) \times \tau \times 10\ \mathrm{kg}$ divided by `flowFraction` (the packet mass as a fraction of a full 10 kg pipe), so $\text{rate}$ and $\tau$ are not independent. Changing $\tau$ alone moves pacing and equilibrium together. Changing $\text{rate}$ and $\tau$ by reciprocal factors moves pacing while
holding equilibrium fixed. The cleaning threshold is a separate gameplay constant (see below, "Deliberate choices", item 4).

## Fluids and byproducts
The fouling model includes five temperature factors that scale deposition up or down based on wall temperature $T_\mathrm{wall}$ in kelvin:

| Mechanism | $f(T_\mathrm{wall})$ | Shape |
|---|---|---|
| biological | $1$ below 345 K (72 °C pasteurization), else $0$ | biofilm grows until pasteurized |
| scaling | $\max(0, (T_\mathrm{wall} - 293)/80)$ | inverse-solubility salts, rises with $T_\mathrm{wall}$ |
| coking | $2^{(T_\mathrm{wall} - 373)/25}$ | Arrhenius stand-in, doubles every 25 K |
| particulate | $1$ | suspended solids settle regardless of $T_\mathrm{wall}$ |
| waxing | $\mathrm{clamp01}((323 - T_\mathrm{wall})/60)$ | wax deposits on a cold wall; full at −10 °C, none at 50 °C |

## Liquid classification
Every liquid in the game's `elements/liquid.yaml` is classified. Where the yaml names a solid that the liquid leaves behind on boiling (`highTempTransitionOreId`), that solid is the byproduct. This mod silently accepts whatever the game already says comes out of the liquid. Ids in the table below are the yaml `elementId`, which may not match the liquid name in-game. Several DLC liquids display under a different name (Brackene = `Milk`, Ovolene = `FishMilk`, Nectar = `SugarWater`, Mucin = `Mucus`, Polluted Brine = `MurkyBrine`, and the `MilkFat` byproduct displays as Brackwax). Rates are kg deposit per kg fluid at $f(T_\mathrm{wall}) = 1$.

| Liquid (id) | Mechanism | Byproduct | Rate | Basis |
|---|---|---|---|---|
| Polluted Water (`DirtyWater`) | biological | Dirt | 1.5e-4 | boils to Dirt 1% |
| Mucin (`Mucus`) | biological | Slime | 3e-4 | boils to Polluted Water + Slime 30% |
| Salt Water | scaling | Salt | 6e-5 | Salt 7% |
| Brine | scaling | Salt | 2.1e-4 | Salt 30% |
| Polluted Brine (`MurkyBrine`) | scaling | Salt | 2.1e-4 | Salt 30% (avoids two byproducts with inverse fouling rates) |
| Nectar (`SugarWater`) | scaling | Sucrose | 2e-4 | Sucrose 77%; sugar crystallizes on hot surfaces |
| Crude Oil | coking | Refined Carbon | 3e-4 | |
| Petroleum | coking | Sulfur | 6e-5 | from existing sour gas chain |
| Naphtha | coking | Refined Carbon | 6e-5 | hydrocarbon, coke is the default |
| Gunk (`LiquidGunk`) | coking | Sulfur | 3e-4 | boils to Petroleum + Sulfur 8% |
| Phyto Oil | coking | Algae | 1.5e-4 | boils to CO2 + Algae 67% at 75 °C, so $f \approx 0.5$ |
| Biodiesel (`RefinedLipid`) | coking | Refined Carbon | 6e-5 | lipid gumming, low |
| Resin | coking | Isoresin | 3e-4 | Isoresin 25%; the game cures resin with heat |
| Natural Resin | coking | Refined Carbon | 3e-4 | Refined Carbon 25% |
| Latex | coking | Rubber | 2e-4 | latex coagulates with heat |
| Ink | particulate | Refined Carbon | 1e-4 | pigment; boils to Mucus + Refined Carbon 10% |
| Brackene (`Milk`) | waxing | Brackwax (`MilkFat`) | 1.5e-4 | boils to Brine + wax 10% |
| Ovolene (`FishMilk`) | waxing | Brackwax (`MilkFat`) | 1.5e-4 | boils to Steam + wax 10% |

Materials with no entry (and reason):
* Water, Ethanol, Super Coolant, Visco-Gel (pure or engineered)
* Liquid Sulfur, Liquid Phosphorus, Mercury, Molten Sucrose, every molten metal, Molten Glass, Molten Salt, Liquid Carbon (single substances)
*  Magma and Liquid Uranium (See the melt rule in [THERMAL.md](THERMAL.md), "Shell heat, insulation, and melting")
* Nuclear Waste (radioactive sludge deposit would be science fiction)
* Chlorine and the cryogens Oxygen, Hydrogen, Methane, Carbon Dioxide,
Propane (nothing dissolved)
* Liquid Helium and Molten Syngas (disabled in the yaml)

Polluted Brine fouls by scaling alone. Scaling is near zero below about 30 °C, so cold Polluted Brine barely fouls even though it would also grow a biofilm in the real world.

Waxing reference: Bott, *Fouling of Heat Exchangers* (1995), solidification fouling; paraffin deposition in crude pipelines is the textbook cold-wall case.

## Pacing
At full 10 kg/s flow with $f(T_\mathrm{wall}) = 1$ the asymptotic deposit reduces to $\text{rate} \times \tau \times 10\ \mathrm{kg}$, so scaling every $\text{rate}$ up and $\tau$ down by the same factor speeds the whole system up without moving any equilibrium (see above, "Calibration").

The current setting was determined by gameplay feel. A throttled copper brine loop reaches the 50% cleaning point in about 19 cycles. Brine at max 10 kg/s flow rate settles near 27% at a 322 K $T_\mathrm{wall}$ and never triggers a cleaning errand. A thermium exchanger on throttled
brine fouls quickly, reaching 50% in about four cycles at 0.34 kg of deposit.

## Deliberate choices
1. **Shear scours only the flowing fluid's own byproduct.** A petroleum packet strips sulfur, not the carbon a crude oil packet left. Mixed streams therefore level off near the sum of the individual asymptotes.
2. **Non-fouling fluids do not scour.** A fouled exchanger cannot be flushed with water, and only a Duplicant cleaning errand empties the plates. At $\tau = 600$ s a full-flow flush would scrub them in about ten minutes. Scale and coke do not rinse off in reality either.
3. **Deposit mass scoured off becomes the flowing element** (carbon back into crude, sulfur into petroleum). This is not realistic, but it is mass-conserving and feels better than spawning debris every tick.
4. **The displayed fouling percent is the cleaning threshold.** It is a
conductance ratio, so a thermium exchanger reads 50% at only 0.34 kg of deposit while its effectiveness has barely moved. Real plants clean on cleanliness factor too, and using the number the player sees keeps the gameplay trigger legible.
5. **Which fluids foul is left to player discovery.** The building description and tooltip name only the fouling mechanisms.

## Cleaning mechanics
What the cleaning errand does:

- An automatic cleaning errand order fires when the displayed integer percent first reaches 50, on the rising edge only. It is re-armed by a completed clean, so a cancelled order is not re-raised, even if fouling stays above the threshold.
- Both streams stop while the plates are open for cleaning. Nothing flows and no heat is exchanged while a Duplicant is working on them. If the Duplicant is interrupted, flow resumes until the work restarts.
- Every ledger empties on errand completion into one debris chunk per byproduct element, at the building's temperature, with exactly the ledger mass; a ledger under one gram is discarded rather than spawned. After cleaning, the ledgers read zero, the fouling readout reads 0%, and $G$ returns to $G_\mathrm{clean}$.
- A byproduct whose element is absent from the game drops nothing.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface fouling. *British Chemical Engineering*, 4(5), 258–262.
- Bott, T. R. (1995). *Fouling of Heat Exchangers*. Elsevier.
- TEMA Standards, fouling resistance tables.
- Element data: `StreamingAssets/elements/liquid.yaml` (ids, `highTempTransitionOreId`,
  `dlcId`, disabled entries).
