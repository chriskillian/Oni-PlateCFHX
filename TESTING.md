# Testing
Verification plan, verification record, and to-do list for the Plate
Counterflow Heat Exchanger. Design and model text is in
[DEVELOPMENT.md](DEVELOPMENT.md), [THERMAL.md](THERMAL.md), and
[FOULING.md](FOULING.md); this file records what
has been checked, what has not, and what remains to build.

## Contents
- [Test rigs](#test-rigs)
- [Verification plan](#verification-plan)
- [Verification record](#verification-record)
- [To do](#to-do)

## Test rigs
- `DebugLog` in `HeatExchangerCore` defaults to false; set it true for `[PCHX]`
  calibration lines in `Player.log` every 30 conduit ticks. It is `readonly`,
  not `const`, so the guarded blocks compile (DEVELOPMENT.md, "Building and
  testing").
- A thermium exchanger on brine throttled to about 2 kg/s against cold water is
  the fastest fouling rig: it reaches the 50% threshold in about four cycles.
- Liquid classification and flow-readout checks used a thermium/Insulite rig at
  2 kg/s each side; the re-arm test used a ceramic brine rig.
- The Soda Fountain is not a control for secondary-port behaviour: it has one
  primary liquid input at `CellOffset(1, 1)` and no secondary port. It was silent
  in the test anyway, most likely because the connect sound plays during the pipe
  build tool's drag and so needs the building to exist before the pipe is laid
  (DEVELOPMENT.md, "Geometry and ports").
- Shell heat was measured on a thermium/Ceramic exchanger in 5 kg/tile oxygen
  with only 275 K water flowing, with a thermium/Insulite exchanger in the same
  save as the adiabatic control.

## Verification plan
Checks not yet run, each with what a pass looks like.

- **Phase-change margin at 2 K** (built, not yet re-verified): run 275 K water
  against a cold brine stream until the water outlet nears 273 K; "Output near
  phase change" names the stream and both temperatures and clears within 5 s of
  the outlet warming. Verified at the earlier 5 K margin; the 2 K margin has not
  been re-tested.
- **Tidying speed**: a high-Tidying Duplicant finishes a clean in well under the
  30 s base. No such Duplicant exists in the test colony; to be observed
  opportunistically.
- **Body tracks the fluid mean; the room warms behind Ceramic and not behind
  Insulite.** From the shell-heat plan. Not verified as stated: the shell
  measurement found the body sitting near footprint air with the shell as the
  limiting resistance (THERMAL.md, "Shell calibration"); the Insulite control
  was adiabatic. Whether a hot fluid warms the room behind Ceramic in ordinary
  air has not been measured directly.

## Verification record
One line per verified item. Numbers are kept where they are calibration
evidence; the model text they support is in THERMAL.md and FOULING.md.

| Item | Date | Evidence |
|---|---|---|
| Dual same-type streams on one building flow simultaneously without mixing | | |
| Thermal model matches hand calculation, copper, full flow, steady state | | $\mathrm{NTU}$ 2.38, $\varepsilon$ 0.75 at 10 kg/s brine vs 10 kg/s water; energy conserved exactly; temperature cross observed (`PackingFactor` $= 150$) |
| Fouling deposition and removal match the model at full and throttled flow | | full-flow brine settles at 0.46 kg (27%) at a 322 K wall; ledgers survive save and reload |
| $\times 3$ pacing ($\tau$ 1800 s to 600 s, rates $\times 3$) | | throttled copper brine reaches 50% in about 19 cycles (was 58) |
| Gasket recipe; research gate (Liquid Tuning); build menu (Utilities, Aquatuner group) | | subcategory warning gone |
| Cleaning UI | | status item and tooltip render; button toggles; errand appears and disappears with the order |
| Manual clean | | Duplicant performs the errand; both pipes back up; one Salt chunk with exactly the ledger mass; fouling 0%; conductance returns to clean; series-resistance formula checked to four figures before and after |
| Pending order and its errand survive save, exit, and reload | | |
| Automatic trigger | 2026-09-07 | fires at exactly 50% on thermium: deposit 0.3369 kg against a predicted 0.3367 kg crossing; cancelled order not re-raised above 50%; completed clean re-arms it and the next crossing fires |
| Automatic trigger re-arm, second pass | 2026-09-08 | ceramic brine rig: auto order at 50%, clean, re-armed, next crossing fired |
| Shell heat, cold-water run | 2026-09-07 | thermium/Ceramic in 5 kg/tile oxygen, 275 K water only, twenty minutes: body 290.7 K against bottom-center footprint oxygen 17.2–17.5 °C (290.4–290.65 K, flickering as gas cells swap) while drawing 7.4 kW; room cooled from 21.6 °C; vanilla leg on the order of 25 kW/K or more; insulation is the limiter by two orders. Supersedes the heating-side estimate of 9 kW/K (1.7 K offset at 16 kW). `ShellFactor` $= 1500$ and default `def.ThermalConductivity` stand |
| Shell heat, Insulite control | 2026-09-07 | same save: body 293.8 K against footprint oxygen 20.7 °C (293.85 K), zero shell exchange; adiabatic |
| Shell step by hand on the first tick | 2026-09-07 | Ceramic fallback on a two-material building, 930 W/K; signs and magnitudes of both packet terms match |
| Three-slot recipe and picker | 2026-09-07 | recipe renders; picker offers all five `Insulator` elements in `buildMenuSort` order (Refined Carbon, Ceramic, Pearl, Rubber, Insulite) |
| `[PCHX] insulator=` log line and `constructionElements[2]` read | 2026-09-07 | `insulator=Ceramic k=0.62 Gshell=930` and `insulator=SuperInsulator k=1E-05` on two new buildings; `G4` format fix prints Insulite as 0.015 W/K |
| Plate melt rule on magma | 2026-09-07 | magma vs molten copper, Ceramic slot: melt logged at 1988 K against 1357 K on the first tick with fluid; notification posted; building gone; 1100 kg copper tile in the origin cell (800 kg copper + 200 kg insulator + $2 \times 50$ kg gaskets). The plate rule fired through Ceramic wrapping, the case the body rule could never catch |
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
| Liquid classification: Ink vs Brackene, thermium/Insulite, 2 kg/s | 2026-09-08 | Ink 0.2 g/tick (particulate, $f = 1$); Brackene 0.039 g/tick at wall 315.2 K (waxing $f = 0.13$); both ledgers, $G$ (284,641 vs 284,650 by hand) and the $\varepsilon = 1$ outlet temperatures matched; cleaning dropped Refined Carbon and Brackwax chunks |
| Liquid classification: Brackene 280 K vs Water 300 K | 2026-09-08 | wall 290 K, $f = 0.55$, deposition 0.165 g/tick, $\varepsilon$ 0.980 at $\mathrm{NTU}$ 34 / $C_r$ 0.981, outlets matched; from a fresh clean 4.9 g per 30 ticks twice in a row (model 4.94 g) |
| Liquid classification: Water 339.5 K vs Brackene 299.8 K | 2026-09-08 | wall 319.7 K, $f = 0.056$, 0.5 g per 30 ticks over three intervals (model 0.50 g). Three points on the waxing line match; the exact cutoff at 323 K was not reached |
| Flow readout (a) | 2026-09-08 | "Flow: A 2 kg/s, B 10 kg/s" on the hover card and side panel against valves set so |
| Flow readout (b) | 2026-09-08 | one valve shut: that rate 0, effectiveness "none (one stream idle)" (the wording at the time) |
| Flow readout (c) | 2026-09-08 | during a clean both read 0; effectiveness read 100%, the last exchanging tick's value (fixed and verified 2026-09-09, below) |
| Flow readout (d) | 2026-09-08 | tooltip effectiveness matched the log: `eps=1.000` at 4.1% fouling, Water 339.5 K vs Brackene 299.8 K, $\mathrm{NTU}$ 34, $C_r$ 0.204 |
| Custom art loads | 2026-09-09 | Player.log: "Successfully loaded from path 'root' with content 'DLL, Animation'", no Missing Anim 0x52261FE0, no [PCHX] fallback warning. Fixes: csproj anim/ prefix, folder PCHX registered as PCHX_kanim, RemoveDir before copy |
| Custom art, first look | 2026-09-09 | body slightly over 3 cells, collars off the pipe endpoints, plan-menu icon building-sized (research and database icons fine), ghost correct, no glints (expected: nothing plays `on`). Diagnosis: art drawn at 120 px/cell, game draws 100 px/cell; fixes in Verification plan |
| Custom art, second look | 2026-09-09 | rendered at 320 px (game draws 100 px/cell): body 3 cells tall, collars on the pipe endpoints, plan-menu icon (128 px ui frame) the size of its neighbours; accepted for now. Construction site not yet reported |
| Building name shortened to "Counterflow Heat Exchanger" | 2026-09-09 | build-menu box no longer crowded |
| Name as codex link | 2026-09-09 | research-progress tooltip lists it in vanilla link colour; clicking the name opens the database entry |
| Construction site | 2026-09-09 | pale blue sketch from `PCHX_place`, no longer the finished body. Cosmetic gap, low priority: vanilla sites are transparent with white lines where the art has dark lines; ours is a tinted, desaturated body |
| Flow animation | 2026-09-09 | glints with one or two valves open; still with both shut; still during a clean |
| Effectiveness while cleaning | 2026-09-09 | "none (no flow)" while the plates are open; $\varepsilon$ returns after the clean |
| Flow readout (e) | 2026-09-09 | output valve at 2 kg/s against a 10 kg/s input; readout showed the accepted 2 kg/s, not the pipe contents |
| Building art, version 2, in game | 2026-09-13 | user screenshot at 118 screen px per cell. Good: plan-menu icon, database entry, research icon. Off: bottom collar ring centres 8 screen px above the pipe centreline (close); building 3.13 cells tall, top bar 19 screen px above the cell-3 boundary; top collar rings 14 screen px above the pipe centreline; construction ghost out of scale with the body, because Codex drew it as a separate image; plate animation too fast; see-through frame interior made the building look shell-less. Measurement method: pipes attached to the ports sit on the cell centres, so the pipe centreline is the reference; the eye aligns on the copper ring centre, which sits about 8 source px above the alpha centroid of the flange component that version 2 aligned on. Fixes in version 3, Verification plan |
| Building art, version 3, in game | 2026-09-13 | user screenshot `ONI_PCHX_screenshot_2.png` at 147 screen px per cell. Much better: top bar 2 screen px under the cell-3 boundary; construction ghost matches the body. Off: both collar pairs slightly low against the attached pipes, top rings 4.5 screen px (3 source px) low and bottom rings 2 screen px (1.4 source px) low; the plate stack drew over the frame's bottom bar, because the source layers overlap by 13 px at rows 295 to 308 and plates were in front of the frame. Fixes in version 3.1, Verification plan |
| Building art, version 3.1, in game | 2026-09-13 | user screenshot `ONI_PCHX_screenshot_3.png` at 123 screen px per cell, with the intended pipe axis marked by a green dot on each flange. Fixed: the plate stack now draws behind the frame. Off: the 3 px port nudge was too subtle, the ports still read misaligned. Diagnosis from a 4x crop: the copper ring and the flange disc are concentric with the attached pipe, both centred at 304 screen px, but the grey stub cylinder the pipe plugs into is drawn about 6 screen px lower, its axis at 310, which matches the green dots; the pipe enters the stub high. The stub axis, not the ring centre, is what the eye aligns. Fixes in version 3.2, Verification plan |
| Building art, version 3.2, in game | 2026-09-13 | ports align with the attached pipes and the construction ghost matches the body, both exactly as expected. Off: the plate pack looked barely full, because the stack ended 9 px short of the insulation shell's edge in the upper and lower sections (shell right edge x 177, plates left edge x 186 in source), showing the dark back panel and the pack's shaded end. Fix in version 4, Verification plan |
| Building art, version 4, in game | 2026-09-13 | better: the pack now reaches under the shell, so the window between shell and post is filled. Off: the mirror-tiled extension disturbed the metallic shine across the plate face. Replaced by user-drawn art in versions 5 and 6 |
| Building art, version 6, in game | 2026-09-13 | body judged good: the pack fills the window out to the insulation shell's edge with the metallic shine intact, tucks under the right post with no back panel showing, and reads at a finer stripe pitch than version 5. Plate animation, port alignment, build icon, research icon and database art all passed. One defect: the blueprint ghost still drew the plate pack on top of the frame's bottom bar, because the ghost's layer arguments had kept the pre-3.1 order since version 3 (ART.md, "History"). Fixed in version 6.1, below. Version 5 was superseded by version 6 before it was ever seen in game |
| Building art, version 6.1, in game | 2026-09-13 | blueprint ghost draws the frame's bottom bar in front of the plate pack, matching the finished body since version 3.1; correct. Nothing else in the art changed, so the version 6 result stands |
| Building art, version 7, in game | 2026-09-13 | steam wisp animation looks good; the still body is unchanged from version 6.1, so those results stand. Art declared complete |
| Duplicant cleaning anim: disinfect spray plays | 2026-09-14 | the Duplicant plays the disinfect multitool spray for the work time. The building animated throughout the clean instead of dropping to `off`, which confirms `HeatExchangerCore.SetCleaningAnim` is called. Two gaps found and both since closed, below: the splash landed on the origin cell, and `working` was a copy of `on` |
| Clean Plates pickup by idle duplicants | 2026-09-14 | `interruptPriority` copied from `EmptyStorage`: two idle duplicants at two exchangers, one duplicant-built and one sandbox-spawned, started the errand immediately on order and both ran to completion; eval log `interrupt(ours/EmptyStorage/current)=96700/96700/96400`, `found=True` |
| Cleaning anim, building side, kanim version 8 | 2026-09-14 | `working` is the plate jitter with the `fx_glow_vapor` timeline deleted: glow and vapor absent for the whole clean, and both return on completion and after a mid-clean cancel. Preview `art/preview/working-v8-noglow.png` (ART.md, "History") |
| Splash aim point | 2026-09-14 | `FoulingCleanWorkable.GetTargetPoint` override returns the 3x3 centre cell, nudged $0.25$ toward the plate pack: the spray hits the middle of the exchanger, slightly right of centre (DEVELOPMENT.md, "Cleaning") |

## To do
Release decisions: `DebugLog` was turned off 2026-09-14, after the liquid
classification work; the "[PCHX] acceptance mismatch" warning is kept permanently (silent
unless the game's pipe acceptance rule changes); the PLib options menu is
deferred, a plain constant covers a fouling switch until someone asks.

**Player-facing**
- Mod options menu: a switch to disable fouling entirely, and a slider for
  `PackingFactor` (effectiveness). PLib's options system is the usual route and
  is usable for options alone even though we avoid it for conduits.
- Localization, stage 2: load our own `translations/<locale>.po` and generate a
  `.pot` template (DEVELOPMENT.md, "Localization", for the signatures still
  needed).
- Codex: a custom section (diagram, fouling curve) would need the codex
  generator decompiled; low priority.

**Model**
- Cleaning spawn: a missing byproduct element is skipped silently; a
  `LogWarning` there would be better (`FoulingCleanWorkable`, user's call).
- Phase-change margin: re-verify at 2 K (Verification plan).

Release checklist: mod_info.yaml version bump pending user decision; rebuild on
Mac and confirm `Player.log` has no `[PCHX]` calibration lines.
