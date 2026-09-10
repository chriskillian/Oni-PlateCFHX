# Art

Custom kanim for the plate counterflow heat exchanger. Companion to
[DEVELOPMENT.md](DEVELOPMENT.md).

The building uses its own kanim, `PCHX_kanim`. The game loads any
`anim/assets/<name>/` folder inside a mod and registers it as kanim `<name>_kanim`
(verified in Player.log 2026-09-09: folder `PCHX_kanim` registered as
`PCHX_kanim_kanim`), so the folder is `anim/assets/PCHX/` and `CreateBuildingDef`
asks for `PCHX_kanim`. If the kanim is missing at def-creation time the config
falls back to `metalrefinery_kanim` and logs `[PCHX] kanim ... not loaded`.

The custom art is wired but has not yet been seen in game. The first in-game
test, with each symptom and its fix, is in TESTING.md, "Verification plan".

**Pipeline.** Source of truth is `art/svg/` (the static body and the four glint
overlays), drawn on a 384 px canvas at 120 px per cell: floor at y=360, centre
x=192, port cell centres at x 72|312 and y 60|300, which is where the collars sit.
The game draws 100 px per cell (first in-game look 2026-09-09: the 300 px body
showed as three cells), so `tools/render_art.sh` renders each SVG to a 320 px
frame in `art/kanim-source/` (`PCHX_body_0.png`, `PCHX_glint_0..3.png`,
`PCHX_place_0.png`, the latter from `place.svg`, a filter over the static SVG that
desaturates, tints blue and fades it, the vanilla under-construction look) and the
icon to a 128 px `ui_0.png`; the plan menu shows the ui sprite at native size and
a 384 px icon dwarfed its neighbours. kanimal takes the symbol name from the PNG
prefix, which is why the icon file is `ui_0.png` and the others `PCHX_*`. The
Spriter project `PCHX.scml` lists each frame with its pixel size and pivot
(0.5, 0.0625): centre bottom, at the feet. `tools/build_kanim.sh` runs kanimal-cli
(kanimal-SE 1.3.31) over it and writes `anim/assets/PCHX/` as `PCHX_build.bytes`,
`PCHX_anim.bytes`, `PCHX_0.png`. kanimal-cli is a .NET Core 3.1 + System.Drawing
tool, so on this Ubuntu it runs in a small Docker image (`pchx-kanimal`, built on
first use from Microsoft's 3.1 runtime image plus libgdiplus). The csproj
`InstallMod` target deletes the installed `anim/` tree and copies `anim/**` into
the Dev mod folder (the destination needs an explicit `anim/` prefix because
MSBuild `%(RecursiveDir)` is only the part matched by `**`). Edit an SVG, run
both scripts, rebuild. The first version of the art and the Spriter project were
produced with Codex on 2026-09-08.

**Contents.** Symbols: `PCHX_body` (1 frame), `PCHX_glint` (4 frames: pale segments
drift down the fins, copper glints move through the lower manifold), `ui` (a
copy of the body for the plan-menu icon). Anims: `idle` (the original 4-frame
glint loop), `off` (body only; what a completed building shows), `on` (the glint loop; `HeatExchangerCore.SetFlowAnim` plays it while liquid moves through either stream), `place` (the blueprint sketch
`PCHX_place`; construction site), `ui` (icon). All frames share pivot
(0.5, 0.0625): centre bottom, 24 px above the tile edge, at the feet.

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
  0.005), which would put the body at about 3.1 cells tall. Inference; the first
  in-game look settles it. If the size is wrong, set `scale_x`/`scale_y` on the
  sprites in the `.scml` and rebuild.
