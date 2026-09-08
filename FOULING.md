# Fouling model
The fouling model of the Plate Counterflow Heat Exchanger: the asymptotic
deposition/removal step, its one gameplay knob, the temperature-factor curves,
the classification of every liquid in the game, and what a clean does to the
ledgers. The player-facing cleaning errand (button, status items, chore type) is
in [README.md](README.md), "Cleaning"; the conductance the ledgers degrade is in
[THERMAL.md](THERMAL.md); verification is recorded in [TESTING.md](TESTING.md).

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
wall: `1/G = 1/G_clean + R_A + R_B` (THERMAL.md, "Counterflow ε-NTU"). The
player sees `1 − G/G_clean`.

## Calibration
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
wall, as observed), so rate and τ are not independent. Change τ alone to move
pacing and equilibrium together; change rate and τ by reciprocal factors to move
pacing while holding every equilibrium fixed. The ×3 pacing change under Pacing
is the second kind. The cleaning threshold (`AutoCleanThresholdPercent`, 50) is a
separate gameplay constant on the workable and is discussed under Deliberate
choices, item 4.

## Fluids and byproducts
Five temperature factors, wall temperature T in kelvin:

| Mechanism | f(T) | Shape |
|---|---|---|
| biological | 1 below 345 K (72 °C pasteurization), else 0 | film grows until pasteurized |
| scaling | max(0, (T − 293)/80) | inverse-solubility salts, rises with T |
| coking | 2^((T − 373)/25) | Arrhenius stand-in, doubles every 25 K |
| particulate | 1 | suspended solids settle regardless of T |
| waxing | clamp01((323 − T)/60) | wax comes out on a cold wall; full at −10 °C, none at 50 °C |

## Liquid classification
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
| Polluted Brine (`MurkyBrine`) | scaling | Salt | 2.1e-4 | Salt 30%; also biological in reality, single mechanism by decision (below) |
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
Uranium (the melt rule's territory; THERMAL.md, "Melting"); Nuclear Waste (a
radioactive sludge deposit would be an invention, not physics; decided
2026-09-08); Chlorine and the cryogens Oxygen, Hydrogen, Methane, Carbon Dioxide,
Propane (nothing dissolved); Liquid Helium and Molten Syngas are disabled in the
yaml.

Polluted Brine stays a single-mechanism entry (decided 2026-09-08). A second,
biological spec would add a two-specs-per-liquid structure for a narrow effect:
scaling is near zero below about 30 °C and biological growth stops above 72 °C,
so the only behaviour change would be cold Polluted Brine fouling slowly with
Dirt instead of not at all. Not worth the complexity on its own; open to player
feedback.

Only two liquids carry a Spaced Out `dlcId` in the yaml (Liquid Uranium, Nuclear
Waste); every other DLC liquid is in the base file unmarked, so availability is
decided by world generation, and a table entry for an absent liquid is harmless.
The cleaning spawn skips a byproduct whose element lookup fails, silently; a
`LogWarning` there would be better (`FoulingCleanWorkable`, user's call; see
TESTING.md, "To do").

Waxing reference: Bott, *Fouling of Heat Exchangers* (1995), solidification
fouling; paraffin deposition in crude pipelines is the textbook cold-wall case.

## Pacing
Asymptotic deposit = deposition rate × τ, so scaling every rate up and τ down by
the same factor speeds the whole system up without moving any equilibrium. The
first test ran at τ = 1800 s with rates a third of the current ones: physically
sane, but a throttled brine loop took about 58 cycles to reach 50% fouling.
Factor 3 applied: τ = 600 s, a throttled copper brine loop now reaches 50% in
about 19 cycles, and full-flow brine still settles near 27% at a 322 K wall.

A thermium exchanger on brine throttled to about 2 kg/s against cold water is the
fastest test rig: it fouls to the 50% threshold in about four cycles (TESTING.md,
"Test rigs").

## Deliberate choices
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
5. **The list of fouling fluids stays out of player-facing text** (decided
   2026-09-07). It would be unworkable in a tooltip once complete, and which
   fluids foul is left to player discovery; the description and tooltip name only
   the mechanisms (scaling, coking, biological growth).

## Cleaning mechanics
What a clean does at the model level; the errand as the player sees it is in
README.md, "Cleaning".

- The automatic order fires when the rounded integer percent
  (`HeatExchangerCore.FoulingPercent`) first reaches `AutoCleanThresholdPercent`
  (50), on the rising edge only. It is re-armed by a completed clean, so a
  cancelled automatic order is not re-raised while fouling stays above the
  threshold, and the next crossing after a clean fires again on its own.
- While the plates are open, both streams stop: the conduit updater plans
  nothing, so neither ledger changes and no heat is exchanged.
- On completion every ledger empties into one debris chunk per byproduct
  element, at the building's temperature, with exactly the ledger mass. The
  ledgers read zero, the fouling readout reads 0%, and `G` returns to `G_clean`.
- A byproduct whose element lookup fails is skipped (see Liquid classification).
- Pacing observations: at τ = 600 s a throttled copper brine loop reaches the
  50% threshold in about 19 cycles; full-flow brine settles near 27% at a 322 K
  wall and never triggers; a thermium exchanger on throttled brine reaches 50%
  in about four cycles, at 0.34 kg of deposit.

## References
- Kern, D. Q. and Seaton, R. E. (1959). A theoretical analysis of thermal surface
  fouling. *British Chemical Engineering*, 4(5), 258–262.
- Bott, T. R. (1995). *Fouling of Heat Exchangers*. Elsevier.
- TEMA Standards, fouling resistance tables.
- Element data: `StreamingAssets/elements/liquid.yaml` (ids, `highTempTransitionOreId`,
  `dlcId`, disabled entries).
