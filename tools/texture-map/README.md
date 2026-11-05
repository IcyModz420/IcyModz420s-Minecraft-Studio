# texture-map

Builds the block texture name table used by the Texture Studio in IcyModz420's Minecraft Studio: the file
name of every block texture in every installed Java release and in Bedrock Edition, and
which names are the same texture. Everything is derived from the game files installed on
this PC and from the app's own mapping tables, verified by pixel identity wherever
possible. Only names, sizes and pixel hashes are written; no image data leaves the game
folders.

## Regenerate

Requirements: Python 3 (standard library only, nothing to install). Run from anywhere:

```
python -I tools/texture-map/selftest.py --oracle <dir>      # optional: decoder self-test
python -I tools/texture-map/build.py                        # writes tools/texture-map/out/
```

`build.py` takes about 15 s with a warm cache (`out/cache/images.json`, pixel hashes keyed
by file sha1; delete it to force a full decode, which adds roughly a minute). Options:

| option | default |
|---|---|
| `--appdata DIR` | `%APPDATA%` (reads `DIR/.minecraft/versions/<v>/<v>.jar` and `version_manifest_v2.json`) |
| `--bedrock DIR` | `C:\XboxGames\Minecraft for Windows\Content\data\resource_packs` |
| `--repo DIR` | the repo root two levels up (reads `MinecraftStudio.Core/Data/...`) |
| `--out DIR` | `tools/texture-map/out` |
| `--cache FILE` | `OUT/cache/images.json` |
| `--keep-pairing-evidence-in-gaps` | off: renames dated inside an installed-jar gap get `evidence=uncertain` |

The decoder oracle comes from the repo's existing WIC script:
`powershell -File tools/png-oracle/make-pngs.ps1 -Out <dir>` writes PNGs in every colour
type and bit depth together with their expected pixels; `selftest.py --oracle <dir>`
compares against them. Without `--oracle` it still runs 2,800 synthetic PNG cases (every
colour type, bit depth and filter, tRNS, Adam7 including images too small for some
passes), 64 TGA cases (types 2/10, 24/32-bit, every origin) and .brarchive/JSON tests.

Which Java releases are used: the ones named in `texmap/sources.py` (`JAVA_RELEASES`)
that have a jar on disk. Add a version there after downloading it in the launcher.
Snapshots and modded folders are never used.

## Outputs

- `out/texture_names.csv`: `canonical,platform,name,from,to,evidence,note`, UTF-8, one row
  per name era.
- `out/report.md`: counts, every disagreement, every unmatched texture on each side,
  uncertain dates, open items.

### CSV columns

- `canonical`: identity of one real texture.
  - Java block textures: the newest installed Java name without folder, e.g. `oak_planks`.
  - Other Java textures that Bedrock keeps under `textures/blocks/` (decorated pot
    patterns, conduit parts, barrier): their Java path, e.g. `entity/decorated_pot/angler_pottery_pattern`,
    `item/barrier`. Item textures follow `item/<name>`.
  - Java block textures removed before the newest release: `java:<last path>`, e.g. `java:blocks/bed_feet_end`.
  - Bedrock-only textures: `bedrock:<path>`, e.g. `bedrock:blocks/camera_front`.
  The build checks that no two real textures share a canonical.
- `platform`: `java`, `java_atlas` (pre-1.5 `terrain.png` tile), `bedrock`.
- `name`: Java path under `textures/` without `.png` (`block/oak_planks`, `blocks/planks_oak`);
  `terrain:<index>` (index = y*16+x); Bedrock path under `textures/` without extension.
- `from`/`to`: inclusive, exact release names. Java: first/last installed release in which
  the file was seen; empty `to` = still present in the newest installed jar. Exceptions,
  always stated in the note: 1.7-era names first seen in 1.8.9 use `from=1.7.2` (1.7.2-1.8.8
  assumed to match 1.8.9); `manual` rows fill gap releases from known facts. Bedrock: `from`
  is the first `vanilla_<version>` layer that contains the file, empty = base `vanilla` pack;
  `to` is always empty (layers never delete files).
- `evidence`:
  - `pixels`: identical RGBA8 pixels (colour of fully transparent pixels ignored); for
    animations identical frames; also a Bedrock file identical to an older release of the
    same Java texture (note says which). For the canonical's own Java row, and for every
    `bedrock:` row, it means "this is the file itself" (nothing was paired).
  - `model`: face-by-face pairing through block models (Java blockstates/models; Bedrock
    `blocks.json` + `terrain_texture.json` via the app's `j2b` table; for 1.12.2 -> 1.16.4 via
    `flattening.csv`).
  - `pixels+model`: both agree.
  - `manual`: a known historical fact from the task brief, with the source in the note.
  - `uncertain`: a rename dated inside an installed-jar gap (pairing method in the note), or
    a pairing based on weaker evidence (same file name, same art up to transparency/tint
    without a model link, closest image in a Bedrock texture array). Read the note.
- `note`: dates and gap ranges, the pairing details, tint relation, sizes, animation frames
  (`.mcmeta`, Bedrock `flipbook_textures.json`), PBR texture sets, format changes.

## How it works

`texmap/` holds the library:

| module | role |
|---|---|
| `png.py`, `tga.py` | pure-Python decoders (PNG: all colour types, depths 1-16, tRNS, Adam7; TGA: types 2/10 (+1/3/9/11), both origins) |
| `brarchive.py` | Bedrock `.brarchive` reader (archive path = folder, entries = files) |
| `jsonc.py` | JSON with `//` and `/* */` comments and trailing commas |
| `pixels.py` | RGBA8 hashes (`sha1(width, height, RGBA)`), tint/alpha/luminance comparison |
| `sources.py`, `scan.py` | jar and pack access, layered Bedrock view, hash cache |
| `javamodels.py` | blockstate variants/multipart, model parent chains, `#var` textures, faces |
| `javahist.py` | consecutive-release comparison: pixel renames, model votes, 1.12 -> 1.13 block links |
| `bedrock.py` | merged `blocks.json`, `terrain_texture.json`, flipbooks, texture sets, renamed ids |
| `pairing.py`, `jbpair.py` | Java <-> Bedrock model votes and the evidence decision |
| `chains.py` | name eras per canonical |
| `legacy.py` | introduction versions from `legacy_blocks.csv` (dates 1.7-era names) |
| `report.py` | `report.md` |

Main rules, in order of strength: identical pixels; model faces; Bedrock arrays/carried
textures only when the art matches; leftover discovery (same art up to transparency or
tint, unique on both sides, enough pixels) as `uncertain`. Single-colour images never
count as pixel evidence. For 1.12.2 -> 1.16.4, old names without pixel or model evidence
are linked through the Bedrock file of the same name (Bedrock kept most pre-1.13 names)
and marked `uncertain`.

Item textures (`textures/item(s)/`, Bedrock `textures/items/`) are in the same CSV with
`item/<name>` canonicals. They are paired by pixel identity only (no item model pairing);
1.12.2 -> 1.16.4 links without identical pixels come from the Bedrock file of the same name
or the same file name and are `uncertain`.

`out/cache/` and `__pycache__/` are ignored by `tools/texture-map/.gitignore`.

The app tables used: `MinecraftStudio.Core/Data/flattening.csv`, `legacy_blocks.csv`,
`mappings/j2b.tsv.gz`, `mappings/java_versions.tsv.gz` (default block states),
`mappings/bedrock_upgrade_schemas.json.gz` (renamed Bedrock ids). They are read only.
