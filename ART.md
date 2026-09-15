# Art

This file is not player-facing documentation. It describes the development history of the custom kanim for the plate counterflow heat exchanger. It is a companion to [DEVELOPMENT.md](DEVELOPMENT.md).

The building uses its own kanim, `PCHX_kanim`. The game loads any
`anim/assets/<name>/` folder inside a mod and registers it as kanim `<name>_kanim`
(verified in Player.log 2026-09-09: folder `PCHX_kanim` registered as
`PCHX_kanim_kanim`), so the folder is `anim/assets/PCHX/` and `CreateBuildingDef`
asks for `PCHX_kanim`. If the kanim is missing at def-creation time the config
falls back to `metalrefinery_kanim` and logs `[PCHX] kanim ... not loaded`.

Version 7 is the current body art. It was seen in game 2026-09-13 and passed
(TESTING.md, "Verification record"), and the body art is complete. Versions 2, 3,
3.1, 3.2, 4, 6 and 6.1 were seen in game the same day; version 5 was superseded
by version 6 before it reached the game. See "History" below. The cleaning work
anim is complete: the duplicant's multitool spray plays and the building's own
`working` clip is version 8, both seen in game 2026-09-14 (TESTING.md,
"Verification record").

`art/CODEX_GUIDANCE.md` (2026-09-13) distils versions 2 through 7 into art rules
for Codex: cell geometry, port-axis alignment, layer order, ghost generation,
plate pack fill and pitch, key timing and the floor line.

**Pipeline.** The SVG pipeline that produced version 1 is retired: `art/svg/`,
`tools/render_art.sh` and the `PCHX_body`/`PCHX_glint` symbols are gone from the
build, and their sources are kept in `art.old/` for reference. `render_art.sh` is
historical. Never run it against the current `art/kanim-source/`; it writes the
retired SVG-era layer names and would overwrite the Codex layers.

**Layers.** The user generated high-resolution PNG layer art and had Codex
compose it into a Spriter project and a kanim. Codex's delivery is kept as-is in
`art.new/plate-frame-heat-exchanger/`. The parts the build needs live in `art/`:
`art/kanim-source/` (`PCHX.scml` plus twelve PNG layers), `art/preview/`
(composites), `art/source/` (user-drawn source images), and `art/layers.json`,
Codex's layer extraction record (stacking order and alpha bounds).

Every layer is 384x384 RGBA on one shared canvas with pivot (0.5, 0.0625):
centre bottom, 24 px above the tile edge, at the feet. `ui_0.png` is 128x128
with the same pivot fractions. kanimal takes the symbol name from the PNG
prefix. Stacking order, back to front: `back_panel`, `fx_glow_vapor` (4 frames;
opacity is baked into the pixels at 50/100/150/200 because kanimal drops Spriter
object alpha; the four wisps sit behind the frame posts and shell, so only their
tips show), `connector_pipes` (four short grey stubs between frame and
collars), `internal_plates`, `background_frame`, `insulation_shell`,
`pipe_flanges`.

**Geometry.** The building is 3x3 and the game draws 100 px per cell with the
origin at the pivot, so on the 384 canvas the port cell centres are x 92|292 and
y 110 for the top pair (offset y=2) and y 310 for the bottom pair (offset y=0).
Every sprite except `ui` carries `scale_x` = `scale_y` = 0.945 in `PCHX.scml`.
Spriter scales about the sprite pivot and kanimal writes it as matrix
$a = d = 0.945$ with $t_x = t_y = 0$, so the feet stay on the floor while the
316 px frame becomes 298.6 px, just under three cells. Codex's original collars
sat half a cell high at the bottom and a few px high at both pairs; four
cumulative `tools/pngtool.py shift` passes on `pipe_flanges_0.png` and
`connector_pipes_0.png` brought the component rows to 57..134 and 267..347, which
is what the in-game check passed on. The alignment reference is the grey stub
cylinder's axis, because the pipe plugs into the stub; the flange alpha centroid
and the copper ring centre both sit above it.

`internal_plates_0.png` is generated from `art/source/more_internal_plates.png`
(1254x1254 RGB with a baked-in light checkerboard, the drawing's native
resolution) by `tools/pngtool.py plates SRC DST SX SY RIGHT TOP`, run as
`plates art/source/more_internal_plates.png
art/kanim-source/internal_plates_0.png 0.1896 0.21923 260 81`. Horizontal scale
0.1896 against vertical 0.21923 squeezes the stripe pitch to 4.1 px; the right
edge at canvas x 260 exclusive tucks the pack 2 px under the right frame post,
which is solid over x 257..282. Result bbox x 157..259, y 81..308: the lit plates
begin at x 176, exactly at the insulation shell's solid edge, and the dark
hatched side face sits hidden under the shell. Re-run this command to reproduce
the layer.

**Version 7** (2026-09-13), the current art; seen in game and passed.

- Symptom after the 6.1 look: the steam wisps read only near the bottom-right
  flange. `fx_glow_vapor` had never been re-aligned after the collar moves, so
  its wisps still sat where Codex's collars were, mostly hidden behind the posts
  and shell. The four frames share one geometry and differ only in baked opacity.
- The wisps' alpha-weighted centroids sat on Codex's collar rows, $y$ 105 top and
  $y$ 261 bottom. The current flange alpha centroids are $y$ 96 and $y$ 307, so
  the cumulative collar shift since Codex is top $-9$ px and bottom $+46$ px. A
  clean gap between the two wisp groups at rows 146..175 makes a split shift safe.
- Fix: every `fx_glow_vapor_N.png` shifted with `tools/pngtool.py shift`, whole
  image $dy = -9$, then rows $\ge 151$ a further $dy = +55$. No horizontal
  change; the flanges never moved in $x$.
- The bottom tail would then have crossed the floor line (pivot row $y$ 360)
  carrying 5.4 % of the layer's alpha, where the game draws it over the tile
  beneath. Alpha now fades linearly over rows 344..359 and is zero from row 360
  down; done inline in Python, because `pngtool` has no fade command.
- Ghost unchanged; it excludes the glow. Rebuilt: atlas 461586 B, both `.bytes`
  unchanged. Previews `art/preview/idle-v7-glow-unscaled.png` and
  `art/preview/working-v7-dark.png`.

**History.** Versions 2 through 7 are dated 2026-09-13, each change against a
symptom from the previous in-game look (TESTING.md, "Verification record"). Kept for the
lessons, not the numbers; earlier previews remain in `art/preview/`.

- Version 2: Codex's kanim as delivered, with the bottom collars shifted down
  half a cell. Aligned on the flange alpha centroid; the ports read wrong, the
  body ran 3.13 cells tall, the plate animation was too fast and the frame
  interior was see-through.
- Version 3: the 0.945 scale; new `back_panel_0.png`, a flat dark panel filling
  the frame interior behind `fx_glow_vapor`, which fixed the see-through look;
  `place_0.png` generated from the body layers by a Sobel trace instead of
  Codex's separate blueprint image, which had been out of scale; `on` and
  `working` key times doubled to 66 ms to slow the plate jitter.
- Version 3.1: collars nudged; `internal_plates` moved behind `background_frame`
  (`z_index` swapped in `PCHX.scml`) because the layers overlap at the frame's
  bottom bar and the plates were drawing over it; the ghost trace moved into
  `tools/pngtool.py ghost` so it is reproducible.
- Version 3.2: collars and stubs moved up onto the stub axis. Ports passed in
  game.
- Version 4: the plate pack ended short of the insulation shell, so it was
  extended leftward by mirror-tiling a band of the lit plate face. The window
  filled, but the tiling disturbed the metallic shine; abandoned.
- Version 5: `internal_plates_0.png` rebuilt from the user's drawing at a
  uniform scale, which preserved the shine. Superseded by version 6 before it
  was seen in game.
- Version 6: the same layer rebuilt at a non-uniform scale, squeezing the stripe
  pitch from version 5's 4.8 px to 4.1 px so the lit plate face reaches the
  insulation shell without version 4's tiling. Pitch was measured by DFT power
  spectrum; the user chose the tighter of two previewed candidates. The drawing
  carries about 20 lit plates, so filling the window at Codex's original 2.86 px
  pitch would have needed about 30. Passed in game.
- Version 6.1: `place_0.png` regenerated with its layer arguments in the body's
  z-order. `tools/pngtool.py ghost` composites the layers it is given
  first-at-back, and every regeneration since version 3 had listed the pre-3.1
  order, plates in front of the frame, so the ghost had been inconsistent with
  the body since 3.1. The ghost argument order must match the anim's z-order,
  front layers last. Passed in game.
- Version 8 (2026-09-14): the `fx_glow_vapor` timeline and its 12 object refs
  deleted from the `working` animation in `PCHX.scml`, so cleaning no longer looks
  like flow; `on` unchanged. Passed in game: glow and vapor are absent for the
  whole clean and return both on completion and after a mid-clean cancel. Preview
  `art/preview/working-v8-noglow.png`, built by stacking the six body layers in
  anim z-order:
  `tools/pngtool.py stack art/preview/working-v8-noglow.png back_panel_0 connector_pipes_0 internal_plates_0 background_frame_0 insulation_shell_0 pipe_flanges_0`.
  `art/preview/working-v7-dark.png` is kept as the reference for how the glow
  reads over dark.

Alignment reference history, for the record: version 2 aligned the flange alpha
centroid, version 3 the copper ring centre, version 3.2 the stub cylinder axis.
The stub axis sits about 5 source px below the ring centre in this art, and it is
what the eye aligns, because the pipe plugs into the stub.

Reference screenshots sit in the workspace root, outside the mod directory:
`ONI_PCHX_screenshot.png` for version 2 at 118 screen px per cell,
`ONI_PCHX_screenshot_2.png` for version 3 at 147 screen px per cell, and
`ONI_PCHX_screenshot_3.png` for version 3.1 at 123 screen px per cell, with the
intended pipe axis marked by a green dot on each flange.

`tools/pngtool.py` is a minimal RGBA PNG reader/writer in pure Python, written
because this EC2 box has no PIL; subcommands `shift`, `stack`, `ghost`, `plates`
and `zoom`. `stack` composites onto light grey, which hides `fx_glow_vapor`
almost entirely; inspect the glow on a dark composite instead, as
`art/preview/working-v7-dark.png` does. `plates` is the plate-pack
normalization, parameterized:
checkerboard key, convex per-row fill, 2 px erosion, box-downscale by
$(s_x, s_y)$, alpha from pixel coverage. `zoom SRC DST X0 X1 Y0 Y1 [Z]` is a
nearest-neighbour crop enlarger for inspection. The reader also accepts 8-bit RGB
PNGs, padding alpha to 255.

`tools/build_kanim.sh` is unchanged. It reads `art/kanim-source/PCHX.scml`, runs
kanimal-cli (kanimal-SE 1.3.31) and writes `anim/assets/PCHX/` as
`PCHX_build.bytes` (912 B), `PCHX_anim.bytes` (11131 B at version 8, 11851 B
through version 7) and the atlas `PCHX_0.png` (2048x1024, 461586 B). The version 2
rebuild produced an `anim.bytes` byte-identical to Codex's, which confirmed the
toolchain; `build.bytes` has not changed since version 3, which changed the scale,
the key times and the layer count. Versions 4 through 7 changed only pixels, so
each of those rebuilds left both `.bytes` sizes alone and only the atlas differed;
version 8 dropped a timeline, which shrank `anim.bytes` and left the atlas alone. kanimal-cli is
a .NET Core 3.1 + System.Drawing tool, so on this Ubuntu it runs in a small
Docker image (`pchx-kanimal`, built on first use from Microsoft's 3.1 runtime
image plus libgdiplus). The csproj `InstallMod` target deletes the installed
`anim/` tree and copies `anim/**` into the Dev mod folder (the destination needs
an explicit `anim/` prefix because MSBuild `%(RecursiveDir)` is only the part
matched by `**`). Edit a layer PNG, run `build_kanim.sh`, rebuild.

**Contents.** Anims:
- `off`: one frame, body only; what a completed building shows.
- `idle`: the same single frame.
- `on`: 12 frames at 15.15 fps, a 792 ms loop. `internal_plates` is translated by
  up to 2 anim units, 1 px, while `fx_glow_vapor` cycles its four frames.
  `HeatExchangerCore.SetFlowAnim` plays it while liquid moves through either
  stream.
- `working`: the same 12-frame plate jitter as `on`, on the dull body, with no
  `fx_glow_vapor` timeline. No heat exchange happens during a clean, and a clip
  distinct from `on` makes the cleaning state and the hand-back visible.
  `HeatExchangerCore.SetCleaningAnim` plays it while a Duplicant works the Clean
  Plates errand, then hands back to `on` or `off` per the remembered flow state.
- `place`: the construction-site blueprint, symbol `place`, a white line sketch
  traced from the body layers in the body's own z-order (see "History"), so
  the ghost outline matches the finished building.
- `ui`: the 128 px plan-menu icon.

kanimal derives an anim's frame rate from its key interval: the rebuilt
`anim.bytes` carries rate 15.15 for `on` and `working` at 66 ms keys, and 30.3
for the single-frame anims. Changing the key times in the `.scml` is how to
change playback speed.

Nine symbols: `back_panel`, `fx_glow_vapor`, `connector_pipes`,
`background_frame`, `internal_plates`, `insulation_shell`, `pipe_flanges`,
`place`, `ui`. Element order inside an anim frame is front-first
(`pipe_flanges` first, `back_panel` last), the same convention as the first
kanim.

**In-game look so far.** Version 1 was seen in game 2026-09-09 (TESTING.md,
"Verification record"). Two engine facts came out of it and still hold: the game
draws 100 px per cell, so a 300 px body showed as three cells; and the plan menu
draws the `ui` sprite at native size, so a 384 px icon dwarfed its neighbours
while a 128 px one matched them.

**Format notes** (BILD v10 / ANIM v5, little-endian, SDBM hashes over the
lower-cased name, a hash-to-name table at the end of each file). Recorded so the
files can be read without kanimal:
- Build symbol frame: `src, dur, img, pivotX, pivotY, pivotW, pivotH, uvX1, uvY1, uvX2, uvY2`.
  kanimal writes pivot `(0, -336, 768, 768)` for a 384-px tile: width and height
  are twice the atlas pixels and the origin sits 24 px above the tile's bottom.
  UV y = 0 is the top of the PNG in kanimal's output.
- Anim frame: `bboxCX, bboxCY, bboxW, bboxH`, then elements
  `symbol, frame, folder, flags, rgba, a b c d tx ty, order`. kanimal writes the
  ANIM header's element and frame totals as 0 and every frame bbox as
  `(192, 192, 384, 384)`; other mods ship this, so the game tolerates it.
- I read the 2x pivot convention as 200 anim units per cell (`animScale`
  0.005), so 100 px of art per cell. The version 2 look confirmed it on the new
  canvas: the 316 px frame measured 3.13 cells. Size is corrected with
  `scale_x`/`scale_y` on the sprites in the `.scml`, then a rebuild.
