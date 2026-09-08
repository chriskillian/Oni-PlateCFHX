# Testing
Verification plan, verification record, and to-do list for the Plate
Counterflow Heat Exchanger. Design and model text is in [README.md](README.md),
[THERMAL.md](THERMAL.md), and [FOULING.md](FOULING.md); this file records what
has been checked, what has not, and what remains to build.

## Contents
- [Test rigs](#test-rigs)
- [Verification plan](#verification-plan)
  - [Custom art: first in-game test](#custom-art-first-in-game-test)
- [Verification record](#verification-record)
- [To do](#to-do)

## Test rigs
- Diagnostics go to `Player.log` as `[PCHX]` lines every 30 conduit ticks while
  `DebugLog` is true in `HeatExchangerCore` (README.md, "Building and testing").
- A thermium exchanger on brine throttled to about 2 kg/s against cold water is
  the fastest fouling rig: it reaches the 50% threshold in about four cycles.
- Liquid classification and flow-readout checks used a thermium/Insulite rig at
  2 kg/s each side; the re-arm test used a ceramic brine rig.
- Shell heat was measured on a thermium/Ceramic exchanger in 5 kg/tile oxygen
  with only 275 K water flowing, with a thermium/Insulite exchanger in the same
  save as the adiabatic control.

## Verification plan
Checks not yet run, each with what a pass looks like.

- **Effectiveness while cleaning** (fix written 2026-09-08, unbuilt): order a
  clean; while the plates are open the flow tooltip reads "none (no flow)";
  after the clean, ε returns once both streams flow. Before the fix the tooltip
  read 100% while the plates were open, the last exchanging tick's value.
- **Phase-change margin at 2 K** (built, not yet re-verified): run 275 K water
  against a cold brine stream until the water outlet nears 273 K; "Output near
  phase change" names the stream and both temperatures and clears within 5 s of
  the outlet warming. Verified at the earlier 5 K margin; the 2 K margin has not
  been re-tested.
- **Flow readout check (e)** (optional): a partial-flow stream (blocked output)
  shows the accepted mass, not the pipe contents.
- **Body tracks the fluid mean; the room warms behind Ceramic and not behind
  Insulite.** From the shell-heat plan. Not verified as stated: the shell
  measurement found the body sitting near footprint air with the shell as the
  limiting resistance (THERMAL.md, "Shell calibration"); the Insulite control
  was adiabatic. Whether a hot fluid warms the room behind Ceramic in ordinary
  air has not been measured directly.
- **Idle-duplicant watch item** (seen once, 2026-09-08, flow-readout build): a
  duplicant stood idle for about a minute before taking a Clean Plates chore,
  then worked it normally. Not seen before. A delayed pickup that resolves on
  its own points at a precondition (reach, schedule block, another chore's
  interrupt) rather than chore ranking; our type copies Empty Storage's
  priority, which a lower ranking would never recover from. Note the duplicant's
  status, its Tidying setting and the building's priority if it recurs.

### Custom art: first in-game test
The custom kanim was wired 2026-09-08 and has not yet been seen in game
(README.md, "Art"). Each symptom names its fix:
- Body sits on the floor, about 3 cells tall, stubs at the four port corners,
  static: done.
- Body the wrong size: `scale_x`/`scale_y` in the `.scml` (3 cells / observed height).
- Plan-menu icon blank: `Def.GetUISprite` wants something other than a `ui`
  anim plus `ui` symbol; decompile it.
- Building invisible, with `Missing anim` or `KAnim` lines in Player.log: the game
  asked for an anim name not in the Art section's list; add it to the `.scml`.
- Preview/construction ghost blank: `place` is named differently; check the log.
- `[PCHX] kanim ... not loaded` in Player.log: the folder did not register; check
  the Dev folder has `anim/assets/plate_counterflow_heat_exchanger/` with three files.

## Verification record
One line per verified item. Numbers are kept where they are calibration
evidence; the model text they support is in THERMAL.md and FOULING.md.

| Item | Date | Evidence |
|---|---|---|
| Dual same-type streams on one building flow simultaneously without mixing | | |
| Thermal model matches hand calculation, copper, full flow, steady state | | NTU 2.38, ε 0.75 at 10 kg/s brine vs 10 kg/s water; energy conserved exactly; temperature cross observed (`PackingFactor = 150`) |
| Fouling deposition and removal match the model at full and throttled flow | | full-flow brine settles at 0.46 kg (27%) at a 322 K wall; ledgers survive save and reload |
| ×3 pacing (τ 1800 s to 600 s, rates ×3) | | throttled copper brine reaches 50% in about 19 cycles (was 58) |
| Gasket recipe; research gate (Liquid Tuning); build menu (Utilities, Aquatuner group) | | subcategory warning gone |
| Cleaning UI | | status item and tooltip render; button toggles; errand appears and disappears with the order |
| Manual clean | | Duplicant performs the errand; both pipes back up; one Salt chunk with exactly the ledger mass; fouling 0%; conductance returns to clean; series-resistance formula checked to four figures before and after |
| Pending order and its errand survive save, exit, and reload | | |
| Automatic trigger | 2026-09-07 | fires at exactly 50% on thermium: deposit 0.3369 kg against a predicted 0.3367 kg crossing; cancelled order not re-raised above 50%; completed clean re-arms it and the next crossing fires |
| Automatic trigger re-arm, second pass | 2026-09-08 | ceramic brine rig: auto order at 50%, clean, re-armed, next crossing fired |
| Shell heat, cold-water run | 2026-09-07 | thermium/Ceramic in 5 kg/tile oxygen, 275 K water only, twenty minutes: body 290.7 K against bottom-center footprint oxygen 17.2–17.5 °C (290.4–290.65 K, flickering as gas cells swap) while drawing 7.4 kW; room cooled from 21.6 °C; vanilla leg on the order of 25 kW/K or more; insulation is the limiter by two orders. Supersedes the heating-side estimate of 9 kW/K (1.7 K offset at 16 kW). `ShellFactor = 1500` and default `def.ThermalConductivity` stand |
| Shell heat, Insulite control | 2026-09-07 | same save: body 293.8 K against footprint oxygen 20.7 °C (293.85 K), zero shell exchange; adiabatic |
| Shell step by hand on the first tick | 2026-09-07 | Ceramic fallback on a two-material building, 930 W/K; signs and magnitudes of both packet terms match |
| Three-slot recipe and picker | 2026-09-07 | recipe renders; picker offers all five `Insulator` elements in `buildMenuSort` order (Refined Carbon, Ceramic, Pearl, Rubber, Insulite) |
| `[PCHX] insulator=` log line and `constructionElements[2]` read | 2026-09-07 | `insulator=Ceramic k=0.62 Gshell=930` and `insulator=SuperInsulator k=1E-05` on two new buildings; `G4` format fix prints Insulite as 0.015 W/K |
| Plate melt rule on magma | 2026-09-07 | magma vs molten copper, Ceramic slot: melt logged at 1988 K against 1357 K on the first tick with fluid; notification posted; building gone; 1100 kg copper tile in the origin cell (800 kg copper + 200 kg insulator + 2×50 kg gaskets). The plate rule fired through Ceramic wrapping, the case the body rule could never catch |
| Pipe-contents transfer heat line in the info panel | 2026-09-07 | not shown for the exchanger and not shown for an Aquatuner in the same save; not surfaced by the current UI; cosmetic, nothing to fix |
| Polish check (a): auto order fires the same second the readout first shows 50% | 2026-09-07 | rounded-percent trigger (`FoulingPercent`, `AutoCleanThresholdPercent`) |
| Polish check (b): "Needs cleaning" appears on cancel, clears on a completed clean | 2026-09-07 | tooltip then cut to two short lines |
| Polish check (c): per-port "No pipe: <port>" items | 2026-09-08 | fourth build loads clean; all four port names correct on the hover card; no vanilla "No Liquid Intake", "Liquid Pipe Empty", or "No liquid output" alongside. History: first build's single item did not name the port and stream A's output raised the vanilla item; second build's spawn-time `Destroy` left permanent vanilla items; third build crashed on the fallback scrub because `NeedLiquidOut` allows multiples and can only be removed by Guid; its log confirmed the prefab strip (consumer, `RequireInputs`, `RequireOutputs` removed; dispenser never present) and the fallback was deleted |
| Polish check (d): "Output near phase change" at the 5 K margin | 2026-09-07 | fired with water out at 272.0 K (below its listed 273.15 K; stayed liquid); named the stream and both temperatures; cleared after brine was warmed to 344 K. Margin since cut to 2 K, not re-tested |
| Polish check (e): errand shows as "Clean Plates"; duplicant status reads "Cleaning heat exchanger plates" | 2026-09-07 | no fallback warning in the log |
| Polish check (f): every string renders after the `STRINGS` refactor; every tooltip fits | 2026-09-07 | building name and description, all status items, both buttons, deposit list; no raw `STRINGS.` keys; first build's fouling tooltip overran both edges, lines shortened, second build fits. Localization stage 1 verified by this check |
| Polish check (g): no "Localization.Initialize not found" or "could not patch" warning | 2026-09-07 | |
| Codex entry | 2026-09-07 | automatic Database entry shows art, DESC, and recipe; the three-paragraph DESC renders |
| Liquid classification: compile | 2026-09-08 | all 18 new `SimHashes` names compiled |
| Liquid classification: Ink vs Brackene, thermium/Insulite, 2 kg/s | 2026-09-08 | Ink 0.2 g/tick (particulate, f = 1); Brackene 0.039 g/tick at wall 315.2 K (waxing f = 0.13); both ledgers, G (284,641 vs 284,650 by hand) and the ε = 1 outlet temperatures matched; cleaning dropped Refined Carbon and Brackwax chunks |
| Liquid classification: Brackene 280 K vs Water 300 K | 2026-09-08 | wall 290 K, f = 0.55, deposition 0.165 g/tick, ε 0.980 at NTU 34 / Cr 0.981, outlets matched; from a fresh clean 4.9 g per 30 ticks twice in a row (model 4.94 g) |
| Liquid classification: Water 339.5 K vs Brackene 299.8 K | 2026-09-08 | wall 319.7 K, f = 0.056, 0.5 g per 30 ticks over three intervals (model 0.50 g). Three points on the waxing line match; the exact cutoff at 323 K was not reached |
| Flow readout (a) | 2026-09-08 | "Flow: A 2 kg/s, B 10 kg/s" on the hover card and side panel against valves set so |
| Flow readout (b) | 2026-09-08 | one valve shut: that rate 0, effectiveness "none (one stream idle)" (the wording at the time) |
| Flow readout (c) | 2026-09-08 | during a clean both read 0; effectiveness read 100%, the last exchanging tick's value (fix unbuilt; see Verification plan) |
| Flow readout (d) | 2026-09-08 | tooltip effectiveness matched the log: eps=1.000 at 4.1% fouling, Water 339.5 K vs Brackene 299.8 K, NTU 34, Cr 0.204 |

## To do
Release decisions (2026-09-08): `DebugLog` stays on until the liquid
classification work is done and is flipped off as the last edit before a public
release; the "[PCHX] acceptance mismatch" warning is kept permanently (silent
unless the game's pipe acceptance rule changes); the PLib options menu is
deferred, a plain constant covers a fouling switch until someone asks.

**Player-facing**
- Mod options menu: a switch to disable fouling entirely, and a slider for
  `PackingFactor` (effectiveness). PLib's options system is the usual route and
  is usable for options alone even though we avoid it for conduits.
- Localization, stage 2: load our own `translations/<locale>.po` and generate a
  `.pot` template (README.md, "Localization", for the signatures still needed).
- Codex: a custom section (diagram, fouling curve) would need the codex
  generator decompiled; low priority.
- Custom art: NOT LOADING (first try 2026-09-08). Player.log at startup:
  `Missing Anim: [0x84F0AB5E]` (the SDBM hash of `plate_counterflow_heat_exchanger`)
  followed by `[PCHX] kanim plate_counterflow_heat_exchanger not loaded; using
  metalrefinery_kanim`, so the fallback works and the folder never registered. Two
  earlier `Missing Anim: [0x31A5D250]` lines match none of our names; probably
  unrelated, check by hashing candidates. To diagnose: (1) confirm the Dev mod folder
  contains `anim/assets/plate_counterflow_heat_exchanger/` with the three files
  (csproj copy may have failed); (2) grep Player.log for other lines mentioning
  `anim`, `kanim` or an exception near mod load; (3) decompile the mod loader's
  animation step (`KMod.Mod`, the method that scans `anim/assets`) and
  `ModUtil.AddKAnim` to confirm the folder layout, file naming (`_0.png`?) and when
  it runs relative to `Assets.GetAnim` in `CreateBuildingDef`. Then run the first
  in-game test (Verification plan). Then: play `on`
  while both streams flow and `off` otherwise (a `KBatchedAnimController.Play`
  call in `HeatExchangerCore`, `KAnim.PlayMode.Loop`); a work anim for cleaning
  so `FoulingCleanWorkable.synchronizeAnims` can go true.

**Model**
- Cleaning spawn: a missing byproduct element is skipped silently; a
  `LogWarning` there would be better (`FoulingCleanWorkable`, user's call).
- Flow-rate readout check (e), optional (Verification plan).
- Effectiveness while cleaning: the `FlowBlocked` branch now resets
  `lastEffectiveness` and the readout reads "none (no flow)" for an idle stream
  or an open plate pack. Unbuilt; verify per the Verification plan.
- Phase-change margin: re-verify at 2 K (Verification plan).
- Watch item: idle duplicant before a Clean Plates pickup (Verification plan).

**Release**
- `DebugLog = false`.
