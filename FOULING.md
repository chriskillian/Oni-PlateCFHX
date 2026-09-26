# Fouling model
This document describes the fouling model of the Plate Counterflow Heat Exchanger. It includes the asymptotic deposition and removal step, its calibration, the temperature-factor curves, the classification of liquids, and what a completed cleaning errand does to the deposits. The overall mod description is [README.md](README.md), and the thermal model that describes the per-tick $\varepsilon$-NTU exchange between the two streams is in [THERMAL.md](THERMAL.md).

## Contents
- [Model](#model)
- [Calibration](#calibration)
- [Fluids and byproducts](#fluids-and-byproducts)
- [Other liquids](#other-liquids)
- [Deliberate choices](#deliberate-choices)
- [Cleaning mechanics](#cleaning-mechanics)
- [References](#references)

## Model
Solids carried in a liquid settle on the plates and slow heat transfer, the same way scale builds up in a kettle. How quickly they settle depends on the liquid, the temperature of the plate wall, and the flow rate. Low flow rates allow more deposits, but high flow rates also scour deposits from the plates. Scouring grows faster with flow than settling does, so the deposits climb toward a ceiling instead of growing forever. The cleaning errand removes deposits and returns the accumulated mass to the player.

The deposition model is asymptotic, after Kern and Seaton. Deposition grows with throughput, while shear removal grows with throughput squared. Thus the overall deposit ceiling is lower at high flow rates.

Fouling accumulation is given by the following formula, per tick, per stream:

$$\text{deposition} = R_{\max} \times f(T_\mathrm{wall}) \times m$$

$$\text{removal} = F^{2} \times D \times \frac{\Delta t}{\tau}$$

where

- $m$ is the packet mass in kilograms (a full pipe carries 10 kg per tick)
- $F = m / 10\ \mathrm{kg}$ is the flow fraction (packet mass as a share of a full pipe)
- $T_\mathrm{wall}$ is the wall temperature estimate
- $f(T_\mathrm{wall})$ is the temperature factor, a multiplier bounded in $[0, 1]$
- $R_{\max}$ is the deposition rate in kg of deposit per kg of fluid at $f(T_\mathrm{wall}) = 1$
- $D$ is the deposit of this byproduct already on this side (in kg)
- $\Delta t$ is the tick interval (1.0 seconds)
- $\tau$ is the removal time constant in seconds, the time scale over which full flow scours deposit away

### Deposition limit

Deposition is capped at the storage's remaining capacity. Any excess stays in the packet and passes through the exchanger. Setting deposition equal to removal, with $m = 10\ \mathrm{kg} \times F$, gives:

$$D^{*} = 10\ \mathrm{kg} \times \frac{R_{\max} f(T_\mathrm{wall}) \tau}{F \Delta t}, \qquad \tau' = \frac{\tau}{F^{2}}$$

where

- $D^{*}$ is the asymptote, the deposit in kg at which removal balances deposition
- $\tau'$ is the approach time constant in seconds

The deposit closes about 63% of the remaining gap to $D^{*}$ every $\tau'$ seconds.

### Deposition mechanisms

| Mechanism | $f(T_\mathrm{wall})$ | Note |
|---|---|---|
| biological | $`\begin{cases} 1 & T_\mathrm{wall} \lt 345\ \mathrm{K} \\ 0 & T_\mathrm{wall} \ge 345\ \mathrm{K} \end{cases}`$ | biofilm grows until pasteurized (~72 °C) |
| scaling | $`\mathrm{clamp}_{[0,1]}\dfrac{T_\mathrm{wall} - 293}{80}`$ | fouling starts at 20 °C, caps at 100 °C (inverse-solubility salts) |
| coking | $`\dfrac{1}{1 + 2^{-(T_\mathrm{wall} - 600)/25}}`$ | Arrhenius rise doubling every 25 K below a knee at 600 K |
| particulate | $1$ | suspended solids settle regardless of $T_\mathrm{wall}$ |
| waxing | $`\mathrm{clamp}_{[0,1]}\dfrac{323 - T_\mathrm{wall}}{60}`$ | full fouling at −10 °C and below, none at 50 °C (wax deposits on a cold wall) |

$T_\mathrm{wall}$ is estimated per mechanism. Scaling, waxing, biological and particulate use the mean of the two inlet temperatures, or the single flowing one when one stream is stopped. Coking uses the hotter flowing inlet. In a counterflow exchanger the hot-end film temperature sets the coking rate, and the hot inlet fixes that film temperature whatever the cold inlet does. Using the mean would make coking respond to the cold inlet, resulting in a steep, arbitrary sensitivity to coolant choice.

State lives in two `Storage` components, one per stream, holding the byproducts as real solid chunks with mass and temperature. The engine serializes them with the building, so deposits survive a save and reload. Thermal resistance is derived from the stored mass, where each kilogram of deposit adds $10^{-5}$ K/W. Mass is therefore the single source of truth for fouling, and cleaning returns exactly what was deposited.

### Fouling impact
Fouling deposits add thermal resistance in series with the clean plate pack:

$$R = R_\mathrm{clean} + R_A + R_B$$

where $R_\mathrm{clean}$ is the resistance of the plates with no deposit, and $R_A$ and $R_B$ are the deposit resistances on each side, all in kelvin per watt. The thermal model works with the plate conductance $G = 1/R$ ([THERMAL.md](THERMAL.md), "Counterflow ε-NTU"). The building status shows the deposit's share of the total resistance, so a high-conductivity metal like thermium displays a high fouling percentage from smaller deposits.

## Calibration
Two constants influence the cleaning interval. $R_{\max}$ sets how fast deposit builds, and $\tau$ sets how hard flow scours it. The exchanger is tuned to about 4.5 cycles between errands at full 10 kg/s flow on steel, and about 6.9 cycles at 5 kg/s. To clean less often, lower $\tau$ (stronger scour) or lower $R_{\max}$. Lowering $\tau$ has a steep impact. Below about 1700 s deposits never reach the cleaning threshold at full flow. Raising $\tau$ makes cleaning more frequent, but never more often than about every 2.9 cycles on steel. To increase cleaning frequency beyond that, raise $R_{\max}$.

These figures hold on steel for a fluid that deposits $8\times10^{-5}$ of each packet, which each fouling mechanism's common fluid reaches at its anchor wall: Brine at 334 K, Crude Oil at 680 K, and Polluted Water, Ink, Brackene and Ovolene anywhere in their full-fouling range. Other fluids scale with their rate.

Cleaning is never forced. An ignored steel exchanger settles at $\varepsilon \approx 0.63$ at full flow and $\varepsilon \approx 0.72$ at 1 kg/s. The cost of ignoring it is reduced effectiveness.

## Fluids and byproducts
Every liquid in the game's `elements/liquid.yaml` is classified. Where the yaml names a solid that the liquid leaves behind on boiling (`highTempTransitionOreId`), that solid is the byproduct. This mod adopts whatever the game already says comes out of the liquid. Rates are kg of deposit per kg of fluid at $f(T_\mathrm{wall}) = 1$. The table includes the `elementId` where it does not match the name shown in-game.

| Liquid (id) | $R_{\max}$ | Mechanism | Byproduct |
|---|---|---|---|
| Polluted Water (`DirtyWater`) | $`8\times10^{-5}`$ | biological | Dirt |
| Mucin (`Mucus`) | $`1.6\times10^{-4}`$ | biological | Slime (`SlimeMold`) |
| Salt Water | $`4.6\times10^{-5}`$ | scaling | Salt |
| Brine | $`1.6\times10^{-4}`$ | scaling | Salt |
| Polluted Brine (`MurkyBrine`) | $`1.6\times10^{-4}`$ | scaling | Salt |
| Nectar (`SugarWater`) | $`1.5\times10^{-4}`$ | scaling | Sucrose |
| Crude Oil | $`8.9\times10^{-5}`$ | coking | Sulfur |
| Petroleum | $`1.8\times10^{-5}`$ | coking | Sulfur |
| Naphtha | $`1.8\times10^{-5}`$ | coking | Sulfur |
| Gunk (`LiquidGunk`) | $`8.9\times10^{-5}`$ | coking | Sulfur |
| Phyto Oil | $`4.4\times10^{-5}`$ | coking | Algae |
| Biodiesel (`RefinedLipid`) | $`1.8\times10^{-5}`$ | coking | Refined Carbon |
| Resin | $`8.9\times10^{-5}`$ | coking | Isoresin |
| Natural Resin | $`8.9\times10^{-5}`$ | coking | Refined Carbon |
| Latex | $`5.9\times10^{-5}`$ | coking | Rubber |
| Ink | $`8\times10^{-5}`$ | particulate | Refined Carbon |
| Brackene (`Milk`) | $`8\times10^{-5}`$ | waxing | Brackwax (`MilkFat`) |
| Ovolene (`FishMilk`) | $`8\times10^{-5}`$ | waxing | Brackwax (`MilkFat`) |

The oil-family liquids deposit sulfur. The oil chain is crude oil to petroleum to sour gas, 1:1 until sour gas condenses into 67% methane and 33% sulfur, so every kilogram of crude oil carries eventual sulfur. Latex does not include a byproduct in the game files, but deposits rubber following the Vulcanizer. Biodiesel also has no game-defined byproduct, so depositing refined carbon is a declared fiction with a real-world basis. Real-world biodiesel is about 77% carbon by mass.

## Other liquids

Materials with no entry (and reason):
- Water, Ethanol, Super Coolant, Visco-Gel (pure or engineered)
- Liquid Sulfur, Liquid Phosphorus, Mercury, Molten Sucrose, every molten metal, Molten Glass, Molten Salt, Liquid Carbon (single substances)
- Magma and Liquid Uranium (see the melt rule in [THERMAL.md](THERMAL.md), "Shell heat, insulation, and melting")
- Nuclear Waste (radioactive sludge deposit would be science fiction)
- Chlorine and the cryogens Oxygen, Hydrogen, Methane, Carbon Dioxide, Propane (nothing dissolved)
- Liquid Helium and Molten Syngas (disabled in the yaml)

Polluted Brine fouls by scaling alone to avoid having two byproducts with inverse temperature factors from one liquid. Scaling is zero below 20 °C, so cold Polluted Brine barely fouls even though it would also grow a biofilm in the real world.

## Deliberate choices
1. **Shear scours only the flowing fluid's own byproduct.** Mixed streams with different byproducts therefore level off near the sum of the individual asymptotes.
2. **Non-fouling fluids do not scour.** A fouled exchanger cannot be flushed with water, and only a Duplicant cleaning errand empties the plates. Scale and coke do not rinse off in reality either.
3. **Deposit mass scoured off becomes the flowing element.** This is not realistic, but it is mass-conserving and feels better than spawning debris every tick.
4. **Scoured mass can back up at the input port.** The exchanger outputs at most 10 kg/s. If scouring would result in an output packet that exceeds 10 kg, the output is capped and the input pipe retains the difference for the next tick. This follows the bridge output priority rule, so it should feel familiar. Full flow always scours toward the equilibrium point, and the final state does not depend on the flow history.
5. **The displayed fouling percent is the cleaning threshold.** It represents the deposit's share of the total resistance, so a thermium exchanger reads 50% at only 0.34 kg of deposit while its effectiveness has barely moved. Real plants clean on cleanliness factor too.
6. **Which fluids foul is left to player discovery.** The building description and tooltip name only the fouling mechanisms.
7. **Deposits are real chunks in two sealed storages.** Each stream has its own `Storage`, insulated so the chunks are thermally frozen while stored, and closed to item removal so no Duplicant fetches a deposit out of the plates.
8. **Deposition carries the packet's temperature.** Mass and its heat leave the fluid together, so deposition neither creates nor destroys energy. Scoured mass comes back at the chunk's temperature and mixes into the packet mass-weighted.
9. **Hot deposits drop hot.** Removed deposits drop at the temperature they were laid down at, so they may change state at once. Sulfur from a 666 K crude stream melts on the floor.

## Cleaning mechanics
What the cleaning errand does:

- An automatic cleaning errand order fires when the rounded fouling readout reaches 50%, on the rising edge only. It is re-armed by a completed clean, so a cancelled order is not re-raised, even if fouling stays above the threshold.
- Both streams stop while the plates are open for cleaning. Nothing flows and no heat is exchanged while a Duplicant is working on them. If the Duplicant is interrupted, flow resumes until the work restarts.
- Completed work empties both storages, dropping each side's chunks at their own temperature, with exactly the stored mass. A chunk under one gram is consumed rather than dropped, so a clean never litters the floor with gram-scale debris. Afterwards the storages read empty, the fouling readout reads 0%, and $G$ returns to $G_\mathrm{clean}$.
- A byproduct whose element is absent from the game drops nothing.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface fouling. *British Chemical Engineering*, 4(5), 258–262.
- Ebert, W. and Panchal, C. B. (1997). Analysis of Exxon crude-oil slip stream coking data. *Fouling Mitigation of Industrial Heat Exchange Equipment*. Threshold fouling, with activation energies of 50 to 70 kJ/mol; the 25 K doubling width of the coking factor corresponds to about 80 kJ/mol at these temperatures (moderate confidence on the figure).
- Bott, T. R. (1995). *Fouling of Heat Exchangers*. Elsevier. Solidification fouling is the waxing reference; paraffin deposition in crude pipelines is the textbook cold-wall case.
- TEMA Standards, fouling resistance tables.
- Element data: `StreamingAssets/elements/liquid.yaml` (ids, `highTempTransitionOreId`, `dlcId`, disabled entries).
