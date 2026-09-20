# Fouling model
This document describes the fouling model of the Plate Counterflow Heat Exchanger. It includes the asymptotic deposition and removal step, its one gameplay knob, the temperature-factor curves, the classification of liquids, and what a completed cleaning errand does to the deposits. The overall mod description is [README.md](README.md), and the thermal model that describes the per-tick $\varepsilon$-NTU exchange between the two streams is in [THERMAL.md](THERMAL.md). Klei's own behaviour is recorded in [ENGINE.md](../ModDev/ENGINE.md); the design record for the deposit redesign, with the options considered and the arithmetic, is [DEPOSIT_TUNING.md](../ModDev/DEPOSIT_TUNING.md).

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

Symbols, per tick, per stream:
* $m$ is the packet mass in kilograms (a full pipe carries 10 kg per tick)
* $F = m / 10\ \mathrm{kg}$ is the flow fraction, the packet mass as a share of a full pipe
* $T_\mathrm{wall}$ is the wall temperature estimate, the temperature of the plate surface the deposit forms on
* $f(T_\mathrm{wall})$ is the temperature factor, a multiplier bounded in $[0, 1]$ that scales deposition down when the wall does not favour the mechanism
* $R_{\max}$ is the per-fluid deposition rate in kg of deposit per kg of fluid at $f = 1$ (`DepositionRate`)
* $D$ is the deposit of this byproduct already on this side, in kg
* $\Delta t$ is the tick interval (1.0 seconds)
* $\tau$ is the removal time constant in seconds, the time scale over which full flow scours deposit away

$$\text{deposition} = R_{\max} \times f(T_\mathrm{wall}) \times m$$

$$\text{removal} = F^{2} \times D \times \frac{\Delta t}{\tau}$$

Deposition is capped at the storage's remaining capacity; the excess stays in the packet and passes through. Setting deposition equal to removal gives the asymptote and its approach time constant:

$$D^{*} = 10\ \mathrm{kg} \times \frac{R_{\max}\, f\, \tau}{F}, \qquad \tau' = \frac{\tau}{F^{2}}$$

Every temperature factor is bounded in $[0, 1]$, so $R_{\max}$ alone sets the ceiling on what one pass can lose:

| Mechanism | $f(T_\mathrm{wall})$ | Shape |
|---|---|---|
| biological | $$\begin{cases} 1 & T_\mathrm{wall} \lt 345\ \mathrm{K} \\ 0 & T_\mathrm{wall} \ge 345\ \mathrm{K} \end{cases}$$ | biofilm grows until pasteurized (~72 °C) |
| scaling | $\mathrm{clamp}_{[0,1]}\!\left((T_\mathrm{wall} - 293)/80\right)$ | inverse-solubility salts; starts at 20 °C, full at 100 °C, flat above: once the salt is out of solution a hotter wall adds nothing |
| coking | $\dfrac{1}{1 + 2^{-(T_\mathrm{wall} - 600)/25}}$ | Arrhenius rise doubling every 25 K below a knee at 600 K, saturating above it as transport rather than reaction becomes the limit |
| particulate | $1$ | suspended solids settle regardless of $T_\mathrm{wall}$ |
| waxing | $$\mathrm{clamp}_{[0,1]}\!\left((323 - T_\mathrm{wall})/60\right)$$ | wax deposits on a cold wall (full at −10 °C and below, none at 50 °C) |

The coking knee sits at the real coking onset, a film temperature of 550 to 650 K. The logistic is written with the growing term in the denominator on purpose, because the algebraically equivalent form $2^{x}/(1 + 2^{x})$ overflows `Mathf.Pow` to infinity on a hot wall and returns NaN, while this form drives the power toward zero there.

$T_\mathrm{wall}$ is estimated per mechanism. Scaling, waxing, biological and particulate use the mean of the two inlet temperatures, or the single flowing one when one stream is stopped. Coking uses the hotter flowing inlet. In a counterflow exchanger the hot-end film temperature sets the coking rate, and the hot inlet fixes that film temperature whatever the cold inlet does. Using the mean made coking respond to the cold inlet, resulting in a steep, arbitrary sensitivity to coolant choice.

State lives in two game `Storage` components, one per stream, holding the byproducts as real solid chunks with mass and temperature. The engine serializes them with the building, so deposits survive a save and a reload. Thermal resistance is derived from the stored mass, where each kilogram of deposit adds $10^{-5}$ K/W. Mass is therefore the single source of truth for fouling, and cleaning hands back exactly what was deposited.

The deposit adds thermal resistance in series with the clean plate pack:

$$R = R_\mathrm{clean} + R_A + R_B$$

$R_\mathrm{clean}$ is the resistance of the plates with no deposit, and $R_A$ and $R_B$ are the deposit resistances on each side, all in kelvin per watt. The thermal model works with the plate conductance $G = 1/R$ ([THERMAL.md](THERMAL.md), "Counterflow ε-NTU"). The building status shows the deposit's share of the total resistance, $(R_A + R_B)/R$, which equals $1 - G/G_\mathrm{clean}$. A metal with a small $R_\mathrm{clean}$ therefore reads a high percentage from a small deposit.

## Calibration
The fouling model has one gameplay knob, the removal time constant $\tau$ (`Fouling.RemovalTimeConstant`, currently 2700 s). At full flow the approach time constant is $\tau$ itself, so a cleaning interval of several cycles needs $\tau$ near that interval; the earlier 600 s put the asymptote on a knife edge just above the cleaning threshold.

| Constant | Value | Role |
|---|---|---|
| `DepositionRate` (per fluid) | table below | kg of deposit per kg of fluid at $f = 1$ |
| `ResistancePerKg` | 1e-5 K/W per kg | converts stored mass to thermal resistance |
| `RemovalTimeConstant` | 2700 s | shear time constant at full flow |
| `ReferenceMassPerTick` | 10 kg | full-pipe flow; shear removal scales with $F^{2}$ |

The anchor rule sets the rates. The common row of each mechanism family deposits $8\times10^{-5}$ of each packet at its anchor wall, which gives a cleaning errand about every 4.5 cycles at full flow on steel. The anchors are brine at $f = 0.51$ on a 334 K mean wall, crude oil at $f = 0.90$ on a 680 K hot inlet, and the bounded families at $f = 1$. Other rows in a family keep their earlier ratios to the common row; a later pass can tune them individually.

$R_{\max}$ may never exceed the fluid's ore fraction in the game's yaml, the solid the game says the liquid leaves behind on boiling (ENGINE.md, "Element transitions from the yaml"). That is a mass-conservation ceiling, not a tuning value; the calibrated rates sit two orders of magnitude below it.

Two masses follow from the resistance constant. $D_{50} = 1/(G_\mathrm{clean} \times \texttt{ResistancePerKg}) = 7407/k$ kg halves the conductance and so reads 50%, where $k$ is the construction metal's thermal conductivity in W/m·K: steel 1.37 kg, copper 1.23 kg, aluminum 0.36 kg, thermium 0.34 kg. Each stream's storage capacity is $99 \times D_{50}$, the mass at which conductance is 1% of clean: about 136 kg on steel, about 33 kg on thermium. It is an accounting bound nobody reaches at the calibrated rates; the asymptote on steel is 2.2 kg at 10 kg/s and 22 kg at 1 kg/s.

Iteration rule: change $\tau$ to move the cleaning interval, change $R_{\max} \times f$ to move the asymptote, and scale both inversely to speed the system up or slow it down without moving the asymptote.

## Fluids and byproducts
Rates are kg of deposit per kg of fluid at $f = 1$. Ids are the yaml `elementId`; the wall column names the temperature estimate the mechanism reads.

| Liquid (id) | $R_{\max}$ | Factor | Wall | Byproduct |
|---|---|---|---|---|
| Polluted Water (`DirtyWater`) | 8e-5 | biological | mean | Dirt |
| Mucin (`Mucus`) | 1.6e-4 | biological | mean | Slime (`SlimeMold`) |
| Salt Water | 4.6e-5 | scaling | mean | Salt |
| Brine | 1.6e-4 | scaling | mean | Salt |
| Polluted Brine (`MurkyBrine`) | 1.6e-4 | scaling | mean | Salt |
| Nectar (`SugarWater`) | 1.5e-4 | scaling | mean | Sucrose |
| Crude Oil | 8.9e-5 | coking | hot inlet | Sulfur |
| Petroleum | 1.8e-5 | coking | hot inlet | Sulfur |
| Naphtha | 1.8e-5 | coking | hot inlet | Sulfur |
| Gunk (`LiquidGunk`) | 8.9e-5 | coking | hot inlet | Sulfur |
| Phyto Oil | 4.4e-5 | coking | hot inlet | Algae |
| Biodiesel (`RefinedLipid`) | 1.8e-5 | coking | hot inlet | Refined Carbon |
| Resin | 8.9e-5 | coking | hot inlet | Isoresin |
| Natural Resin | 8.9e-5 | coking | hot inlet | Refined Carbon |
| Latex | 5.9e-5 | coking | hot inlet | Rubber |
| Ink | 8e-5 | particulate | mean | Refined Carbon |
| Brackene (`Milk`) | 8e-5 | waxing | mean | Brackwax (`MilkFat`) |
| Ovolene (`FishMilk`) | 8e-5 | waxing | mean | Brackwax (`MilkFat`) |

Crude oil and naphtha deposit sulfur. The oil chain is crude to petroleum to sour gas, 1:1 until sour gas condenses into 67% methane and 33% sulfur, so every kilogram of crude carries eventual sulfur and no carbon anywhere (ENGINE.md, "Element transitions from the yaml"). Two byproducts are not in the yaml: latex to rubber follows the Vulcanizer 1:1, and biodiesel to refined carbon is a declared fiction with a real-world basis, biodiesel being about 77% carbon by mass, so its ceiling is 0.77.

## Liquid classification
Every liquid in the game's `elements/liquid.yaml` is classified. Where the yaml names a solid that the liquid leaves behind on boiling (`highTempTransitionOreId`), that solid is the byproduct. This mod silently accepts whatever the game already says comes out of the liquid. Several DLC liquids display under a different name from their id: Brackene = `Milk`, Ovolene = `FishMilk`, Nectar = `SugarWater`, Mucin = `Mucus`, Polluted Brine = `MurkyBrine`, and the `MilkFat` byproduct displays as Brackwax.

Materials with no entry (and reason):
* Water, Ethanol, Super Coolant, Visco-Gel (pure or engineered)
* Liquid Sulfur, Liquid Phosphorus, Mercury, Molten Sucrose, every molten metal, Molten Glass, Molten Salt, Liquid Carbon (single substances)
* Magma and Liquid Uranium (see the melt rule in [THERMAL.md](THERMAL.md), "Shell heat, insulation, and melting")
* Nuclear Waste (radioactive sludge deposit would be science fiction)
* Chlorine and the cryogens Oxygen, Hydrogen, Methane, Carbon Dioxide, Propane (nothing dissolved)
* Liquid Helium and Molten Syngas (disabled in the yaml)

Polluted Brine fouls by scaling alone; this avoids two byproducts with inverse temperature factors on one liquid. Scaling is zero below 20 °C, so cold Polluted Brine barely fouls even though it would also grow a biofilm in the real world.

## Pacing
At full 10 kg/s flow on steel the interval to a cleaning errand is about 4.5 cycles; the rig measured 4.3 cycles, the errand firing where the rounded display reaches the threshold at 49.5% of true fouling. Throttling lengthens the interval: about 6.4 cycles by the model at 5 kg/s against 6.2 measured, about 15 cycles at 2 kg/s, and about 29 cycles at 1 kg/s. At 1 kg/s shear removal is negligible and the interval is simply $D_{50}$ divided by the deposition rate.

The interval is sensitive to the rates. With $D^{*}/D_{50} \approx 1.6$, a 1.5× rate change moves the full-flow interval from 4.5 cycles to 2.5, and 0.67× moves it to 13 cycles. This amplification is accepted for now; the lever against it is $\tau$, which pushes the asymptote further above the threshold.

Cleaning is never forced. An ignored steel exchanger settles at $\varepsilon \approx 0.63$ at full flow and $\varepsilon \approx 0.72$ at 1 kg/s; the cost of ignoring it is heater load, not a stall. Thermium reads 50% at only 0.34 kg and so cleans about four times as often as steel while its effectiveness has barely dropped. That is kept for now; an effectiveness-based threshold is the noted alternative (DEPOSIT_TUNING.md, decision 3A).

## Deliberate choices
1. **Shear scours only the flowing fluid's own byproduct.** A petroleum packet strips sulfur, not the carbon a crude oil packet left. Mixed streams therefore level off near the sum of the individual asymptotes.
2. **Non-fouling fluids do not scour.** A fouled exchanger cannot be flushed with water, and only a Duplicant cleaning errand empties the plates. At $\tau = 2700$ s a full-flow flush would otherwise scrub them in under an hour. Scale and coke do not rinse off in reality either.
3. **Deposit mass scoured off becomes the flowing element.** This is not realistic, but it is mass-conserving and feels better than spawning debris every tick.
4. **Scoured mass can back up at the input port.** The exchanger outputs at most 10 kg/s. If scouring would result in an output packet that exceeds 10 kg, the output is capped and the input pipe retains the difference for the next tick. This follows the bridge output priority rule, so it should feel familiar. Full flow always scours toward the equilibrium point, and the final state does not depend on the flow history.
5. **The displayed fouling percent is the cleaning threshold.** It is a conductance ratio, so a thermium exchanger reads 50% at only 0.34 kg of deposit while its effectiveness has barely moved. Real plants clean on cleanliness factor too, and using the number the player sees keeps the gameplay trigger legible.
6. **Which fluids foul is left to player discovery.** The building description and tooltip name only the fouling mechanisms.
7. **Deposits are real chunks in two sealed storages.** Each stream has its own `Storage`, shown in the contents panel, insulated so the chunks are thermally frozen while stored, and closed to item removal so no Duplicant fetches a deposit out of the plates.
8. **Deposition carries the packet's temperature.** Mass and its heat leave the fluid together, the game's own convention on a state change, so deposition neither creates nor destroys energy. Scour is the mirror image: returned mass comes back at the chunk's temperature and mixes into the packet mass-weighted.
9. **Hot deposits drop hot.** Sulfur laid down by a 666 K crude stream drops at 666 K, melts at 388.35 K and flashes to sulfur gas at 610.15 K, then recondenses wherever it cools. This is accepted as honest: hot-side debris is as hot as any other object beside a hot exchanger.
10. **The wall estimate is chosen per mechanism.** Scaling, waxing, biological and particulate read the mean of the inlets; coking reads the hotter flowing inlet (see "Model").

## Cleaning mechanics
What the cleaning errand does:

- An automatic cleaning errand order fires when the rounded integer percent the player sees first reaches the threshold, so at 49.5% of true fouling, on the rising edge only. It is re-armed by a completed clean, so a cancelled order is not re-raised, even if fouling stays above the threshold.
- Both streams stop while the plates are open for cleaning. Nothing flows and no heat is exchanged while a Duplicant is working on them. If the Duplicant is interrupted, flow resumes until the work restarts.
- Completed work empties both storages, dropping each side's chunks at their own temperature, with exactly the stored mass. A chunk under one gram is consumed rather than dropped, so a clean never litters the floor with gram-scale debris. Afterwards the storages read empty, the fouling readout reads 0%, and $G$ returns to $G_\mathrm{clean}$.
- Deconstruction and melting drop the deposits too. The engine drops `Storage` contents on both paths, so the mass is never deleted (ENGINE.md, "Buildings, sim heat, plan screen, research").
- A byproduct whose element is absent from the game drops nothing.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface fouling. *British Chemical Engineering*, 4(5), 258–262.
- Ebert, W. and Panchal, C. B. (1997). Analysis of Exxon crude-oil slip stream coking data. *Fouling Mitigation of Industrial Heat Exchange Equipment*. Threshold fouling, with activation energies of 50 to 70 kJ/mol; the 25 K doubling width of the coking factor corresponds to about 80 kJ/mol at these temperatures (moderate confidence on the figure).
- Bott, T. R. (1995). *Fouling of Heat Exchangers*. Elsevier. Solidification fouling is the waxing reference; paraffin deposition in crude pipelines is the textbook cold-wall case.
- TEMA Standards, fouling resistance tables.
- Element data: `StreamingAssets/elements/liquid.yaml` (ids, `highTempTransitionOreId`, `dlcId`, disabled entries), summarized in ENGINE.md, "Element transitions from the yaml".
