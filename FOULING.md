# Fouling model
The fouling model of the Plate Counterflow Heat Exchanger: the asymptotic
deposition and removal step, its one gameplay knob, the temperature-factor
curves, the classification of every liquid in the game, and what a clean does to
the ledgers. The conductance the ledgers degrade is in
[THERMAL.md](THERMAL.md), "Counterflow ε-NTU"; the cleaning errand as the code
builds it is in [DEVELOPMENT.md](DEVELOPMENT.md), "Cleaning".

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
Solids carried in a liquid settle on the plates and slow the heat transfer, the
way scale builds up in a kettle. How fast they settle depends on which liquid it
is and on how hot the plate wall is, and slow flow lets more of it stick. Flow
also scours deposit back off the plates, and scouring grows faster with flow than
settling does, so the deposit climbs toward a ceiling instead of growing forever;
that ceiling is lower at high flow. Cleaning empties the deposit and hands the
mass back to the player.

Asymptotic, after Kern and Seaton: deposition grows with throughput, shear removal
grows with throughput squared, so the deposit levels off, and levels off lower at
high flow.

Per tick, per stream, for a fluid with a table entry, with packet mass $m$ in
kilograms (a full pipe carries 10 kg per tick), wall
temperature $T_\mathrm{wall}$ (the temperature of the plate surface the deposit
forms on), temperature factor $f(T_\mathrm{wall})$ (a multiplier that scales deposition up or
down with that temperature; the curves are under "Fluids and byproducts"),
deposit already on the
wall $D$ in kilograms, tick length $\Delta t$ in seconds, per-fluid deposition
$\text{rate}$ in kilograms of deposit per kilogram of fluid, and removal time
constant $\tau$ in seconds (the time scale over which full flow scours deposit
away):

$$\text{deposition} = \text{rate} \times f(T_\mathrm{wall}) \times m$$

$$\text{removal} = \left(\frac{m}{10\,\mathrm{kg}}\right)^{2} \times D \times \frac{\Delta t}{\tau}$$

$T_\mathrm{wall}$ is the mean of the two inlet temperatures, or the single
flowing one.

State is a **mass ledger** per stream: kilograms of deposit per byproduct
element, saved with the building. Thermal resistance is derived from mass; each
kilogram of deposit adds $10^{-5}$ K/W, so mass is the single source of truth and cleaning can
hand back exactly what was deposited. Resistances add in series with the clean
wall: $1/G = 1/G_\mathrm{clean} + R_A + R_B$ (THERMAL.md, "Counterflow ε-NTU"),
where $G_\mathrm{clean}$ is the conductance of the plate pack with no deposit on
it, $G$ is its conductance with the deposit, and $R_A$ and $R_B$ are the
resistances of the deposit on each of the two streams, in kelvin per watt. The
player sees $1 - G/G_\mathrm{clean}$.

## Calibration
The fouling model has one gameplay knob: the removal time constant $\tau$
(`Fouling.RemovalTimeConstant`, currently 600 s). $\tau$ sets how fast the deposit
approaches its asymptote, and therefore how many cycles pass before a player sees
the cleaning chore. It does not set where the asymptote lands.

The other constants are physical, not gameplay:

| Constant | Value | Role |
|---|---|---|
| `DepositionRate` (per fluid) | table below | kg of deposit per kg of fluid, before $f(T_\mathrm{wall})$ |
| `ResistancePerKg` | 1e-5 K/W per kg | converts ledger mass to thermal resistance |
| `ReferenceMassPerTick` | 10 kg | full-pipe flow; shear removal scales with $(m / 10\,\mathrm{kg})^{2}$ |

Setting deposition equal to removal gives the asymptotic deposit
$\text{rate} \times f(T_\mathrm{wall}) \times \tau \times 10\,\mathrm{kg}$ divided
by `flowFraction`, the packet mass as a fraction of a full 10 kg pipe, so rate and
$\tau$ are not independent. Change $\tau$ alone to move pacing and equilibrium
together; change rate and $\tau$ by reciprocal factors to move pacing while
holding every equilibrium fixed. The cleaning threshold
(`AutoCleanThresholdPercent`, 50) is a separate gameplay constant, discussed under
"Deliberate choices", item 4.

## Fluids and byproducts
Five temperature factors, wall temperature $T_\mathrm{wall}$ in kelvin:

| Mechanism | $f(T_\mathrm{wall})$ | Shape |
|---|---|---|
| biological | $1$ below 345 K (72 °C pasteurization), else $0$ | film grows until pasteurized |
| scaling | $\max(0, (T_\mathrm{wall} - 293)/80)$ | inverse-solubility salts, rises with $T_\mathrm{wall}$ |
| coking | $2^{(T_\mathrm{wall} - 373)/25}$ | Arrhenius stand-in, doubles every 25 K |
| particulate | $1$ | suspended solids settle regardless of $T_\mathrm{wall}$ |
| waxing | $\mathrm{clamp01}((323 - T_\mathrm{wall})/60)$ | wax comes out on a cold wall; full at −10 °C, none at 50 °C |

## Liquid classification
Every liquid in the game's `elements/liquid.yaml` (52 entries in build U59) is
classified. Where the yaml names a solid that the liquid leaves
behind on boiling (`highTempTransitionOreId`), that solid is the byproduct: the
game already says what comes out of the liquid. Ids are the yaml `elementId`;
several DLC liquids display under a different name (Brackene = `Milk`, Ovolene =
`FishMilk`, Nectar = `SugarWater`, Mucin = `Mucus`, Polluted Brine = `MurkyBrine`;
the `MilkFat` byproduct displays as Brackwax). Rates are kg deposit per kg fluid
at $f(T_\mathrm{wall}) = 1$.

| Liquid (id) | Mechanism | Byproduct | Rate | Basis |
|---|---|---|---|---|
| Polluted Water (`DirtyWater`) | biological | Dirt | 1.5e-4 | boils to Dirt 1% |
| Mucin (`Mucus`) | biological | Slime | 3e-4 | boils to Polluted Water + Slime 30% |
| Salt Water | scaling | Salt | 6e-5 | Salt 7% |
| Brine | scaling | Salt | 2.1e-4 | Salt 30% |
| Polluted Brine (`MurkyBrine`) | scaling | Salt | 2.1e-4 | Salt 30%; also biological in reality, single mechanism by decision (below) |
| Nectar (`SugarWater`) | scaling | Sucrose | 2e-4 | Sucrose 77%; sugar crystallizes on hot surfaces |
| Crude Oil | coking | Refined Carbon | 3e-4 | |
| Petroleum | coking | Sulfur | 6e-5 | vanilla crude → petroleum → sour gas → sulfur chain |
| Naphtha | coking | Refined Carbon | 6e-5 | hydrocarbon, no ore field; coke is the default |
| Gunk (`LiquidGunk`) | coking | Sulfur | 3e-4 | boils to Petroleum + Sulfur 8% |
| Phyto Oil | coking | Algae | 1.5e-4 | boils to CO2 + Algae 67% at 75 °C, so $f \approx 0.5$ |
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
Uranium (the melt rule's territory; THERMAL.md, "Shell heat, insulation, and
melting"); Nuclear Waste (a radioactive sludge deposit would be an invention, not
physics); Chlorine and the cryogens Oxygen, Hydrogen, Methane, Carbon Dioxide,
Propane (nothing dissolved); Liquid Helium and Molten Syngas are disabled in the
yaml.

Polluted Brine fouls by scaling alone. Scaling is near zero below about 30 °C, so
cold Polluted Brine barely fouls even though a real one would also grow a film.

Only two liquids carry a Spaced Out `dlcId` in the yaml (Liquid Uranium, Nuclear
Waste); every other DLC liquid is in the base file unmarked, so availability is
decided by world generation, and a table entry for an absent liquid is harmless.

Waxing reference: Bott, *Fouling of Heat Exchangers* (1995), solidification
fouling; paraffin deposition in crude pipelines is the textbook cold-wall case.

## Pacing
At full flow with $f(T_\mathrm{wall}) = 1$ the asymptotic deposit reduces to
$\text{rate} \times \tau$, so scaling every rate up and $\tau$ down by the same
factor speeds the whole system up without moving any equilibrium (see
"Calibration").

What the current constants feel like in play: a throttled copper brine loop
reaches the 50% cleaning point in about 19 cycles; full-flow brine settles near
27% at a 322 K wall and never triggers a clean; a thermium exchanger on throttled
brine reaches 50% in about four cycles, at 0.34 kg of deposit.

## Deliberate choices
1. **Shear scours only the flowing fluid's own byproduct.** A petroleum packet
   strips sulfur, not the carbon a crude packet left. Mixed streams therefore
   level off near the sum of the individual asymptotes.
2. **Non-fouling fluids do not scour.** A fouled exchanger cannot be flushed with
   water; only a Duplicant clean empties the plates. At $\tau = 600$ s a full-flow
   flush would scrub them in about ten minutes. Scale and coke do not rinse off in
   reality either.
3. **Deposit mass scoured off becomes the flowing element** (carbon back into
   crude, sulfur into petroleum). A fiction, but mass-conserving and better than
   spawning debris every tick.
4. **The displayed fouling percent is the cleaning threshold.** It is a
   conductance ratio, so a thermium exchanger reads 50% at only 0.34 kg of deposit
   while its effectiveness has barely moved, and lead needs 2.1 kg for the same
   reading while its effectiveness is sensitive to every gram. Real plants clean on
   cleanliness factor too, and using the number the player sees keeps the trigger
   legible.
5. **Which fluids foul is left to player discovery.** The building description and
   tooltip name only the mechanisms: scaling, coking, biological growth.

## Cleaning mechanics
What a clean does at the model level; the errand as the code builds it is in
DEVELOPMENT.md, "Cleaning".

- The automatic order fires when the displayed integer percent first reaches 50,
  on the rising edge only. It is re-armed by a completed clean, so a cancelled
  order is not re-raised while fouling stays above the threshold, and the next
  crossing after a clean fires again on its own.
- While the plates are open, both streams stop: the conduit updater plans
  nothing, so neither ledger changes and no heat is exchanged.
- On completion every ledger empties into one debris chunk per byproduct
  element, at the building's temperature, with exactly the ledger mass. The
  ledgers read zero, the fouling readout reads 0%, and $G$ returns to $G_\mathrm{clean}$.
- A byproduct whose element is absent from the game drops nothing (see "Liquid
  classification").

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface
  fouling. *British Chemical Engineering*, 4(5), 258–262.
- Bott, T. R. (1995). *Fouling of Heat Exchangers*. Elsevier.
- TEMA Standards, fouling resistance tables.
- Element data: `StreamingAssets/elements/liquid.yaml` (ids, `highTempTransitionOreId`,
  `dlcId`, disabled entries).
