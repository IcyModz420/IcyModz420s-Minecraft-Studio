# Block texture name table: generation report

Generated 2026-11-01 17:50 by `tools/texture-map/build.py` (Python 3.15.0) from the game files installed on this PC. Output: `out/texture_names.csv` (7282 rows). Names, sizes and pixel hashes only; no image data is stored.

Read this first:

- Newest Java release with a jar on this PC: **26.1.2**. The launcher manifest lists 26.3 as the latest release, but its jar is not downloaded (see Inputs), so "canonical" means the 26.1.2 name.
- No jars exist for 1.13-1.16.3. Every rename across 1.12.2 -> 1.16.4 is dated only to that range and its rows carry `evidence=uncertain` with the pairing method in the note (see "Uncertain dates").
- 1.5-1.6.4 names: not available (no jar installed). No rows were invented for that era.
- `from`/`to` are the first/last installed release in which the file was seen, except: 1.7-era names first seen in 1.8.9 use `from=1.7.2` (assumption stated in each note), and `manual` rows fill a few gap releases from the historical facts given in the brief.
- Item textures are included (canonical `item/<name>`) but paired by pixel identity only; see "Items".

## Inputs

### Java

| version | block textures | folder | pack format |
|---|---|---|---|
| 1.8.9 | 373 | `blocks/` | (no version.json) |
| 1.10.2 | 415 | `blocks/` | (no version.json) |
| 1.12.2 | 478 | `blocks/` | (no version.json) |
| 1.16.4 | 694 | `block/` | 6 |
| 1.17.1 | 818 | `block/` | {'resource': 7, 'data': 7} |
| 1.18 | 818 | `block/` | {'resource': 8, 'data': 8} |
| 1.18.1 | 818 | `block/` | {'resource': 8, 'data': 8} |
| 1.18.2 | 818 | `block/` | {'resource': 8, 'data': 9} |
| 1.19 | 858 | `block/` | {'resource': 9, 'data': 10} |
| 1.19.2 | 858 | `block/` | {'resource': 9, 'data': 10} |
| 1.19.3 | 875 | `block/` | {'resource': 12, 'data': 10} |
| 1.19.4 | 895 | `block/` | {'resource': 13, 'data': 12} |
| 1.20 | 928 | `block/` | {'resource': 15, 'data': 15} |
| 1.20.1 | 928 | `block/` | {'resource': 15, 'data': 15} |
| 1.21 | 1012 | `block/` | {'resource': 34, 'data': 48} |
| 1.21.1 | 1012 | `block/` | {'resource': 34, 'data': 48} |
| 1.21.11 | 1111 | `block/` | {'resource_major': 75, 'resource_minor': 0, 'data_major': 94, 'data_minor': 1} |
| 26.1.2 | 1112 | `block/` | {'resource_major': 84, 'resource_minor': 0, 'data_major': 101, 'data_minor': 1} |
| b1.0 | atlas `terrain.png` | - | (no version.json) |

Releases named in the brief without a jar on disk (folder contents shown):

- 1.2.1: 1.2.1.json
- 1.19.1: natives
- 1.21.3: 1.21.3.json
- 26.1.1: 26.1.1.json
- 26.2: 26.2.json
- 26.3: 26.3.json

Snapshots, pre-releases, release candidates and modded folders were ignored as instructed.

### Bedrock

56 vanilla layers, applied in version order (later overrides earlier): `vanilla`, `vanilla_1.14`, `vanilla_1.15`, `vanilla_1.16`, `vanilla_1.16.100`, `vanilla_1.16.200`, `vanilla_1.16.210`, `vanilla_1.16.220`, `vanilla_1.17.0`, `vanilla_1.17.10`, `vanilla_1.17.20`, `vanilla_1.17.30`, `vanilla_1.17.40`, `vanilla_1.18.0`, `vanilla_1.18.10`, `vanilla_1.18.20`, `vanilla_1.18.30`, `vanilla_1.19.0`, `vanilla_1.19.10`, `vanilla_1.19.20`, `vanilla_1.19.30`, `vanilla_1.19.40`, `vanilla_1.19.50`, `vanilla_1.19.60`, `vanilla_1.19.70`, `vanilla_1.19.80`, `vanilla_1.20.0`, `vanilla_1.20.10`, `vanilla_1.20.20`, `vanilla_1.20.30`, `vanilla_1.20.40`, `vanilla_1.20.50`, `vanilla_1.20.60`, `vanilla_1.20.70`, `vanilla_1.20.80`, `vanilla_1.21.0`, `vanilla_1.21.10`, `vanilla_1.21.20`, `vanilla_1.21.30`, `vanilla_1.21.40`, `vanilla_1.21.50`, `vanilla_1.21.60`, `vanilla_1.21.70`, `vanilla_1.21.80`, `vanilla_1.21.90`, `vanilla_1.21.100`, `vanilla_1.21.110`, `vanilla_1.21.120`, `vanilla_1.21.130`, `vanilla_1.26.0`, `vanilla_1.26.10`, `vanilla_1.26.20`, `vanilla_1.26.30`, `vanilla_1.26.40`, `vanilla_1.26.50`, `vanilla_1.26.60`.

Packs not used, and what they hold under `textures/blocks/`:

| pack | block images | sample | block JSON |
|---|---|---|---|
| beta | 0 | - | - |
| chemistry | 180 | `chemical_heat`, `compound_creator_inside`, `compound_creator_inside_bottom`, `compound_creator_side_a`, `compound_creator_side_b`, `compound_creator_side_c` | blocks.json, textures/terrain_texture.json |
| chemistry_1.20.50 | 0 | - | blocks.json, textures/terrain_texture.json |
| chemistry_1.20.60 | 0 | - | blocks.json, textures/terrain_texture.json |
| chemistry_1.21.0 | 0 | - | - |
| chemistry_1.21.10 | 0 | - | - |
| chemistry_1.21.20 | 0 | - | - |
| chemistry_1.21.30 | 0 | - | blocks.json, textures/terrain_texture.json |
| chemistry_1.26.40 | 0 | - | - |
| editor | 0 | - | - |
| experimental_poi | 0 | - | - |
| experimental_y_2026_drop_2 | 0 | - | - |
| experimental_y_2026_drop_3 | 0 | - | - |
| experimental_y_2026_drop_4 | 16 | `ice_crystal_0`, `ice_crystal_0_mers`, `icicle_base`, `icicle_base_mers`, `icicle_frustrum`, `icicle_frustrum_mers` | blocks.json, textures/terrain_texture.json |
| oreui | 0 | - | - |
| persona | 0 | - | - |
| platform_gdk_pc | 0 | - | - |
| script_ui | 0 | - | - |
| vanilla_base | 0 | - | - |
| vanilla_music | 0 | - | - |
| vanilla_trial | 0 | - | - |

None of these is part of the vanilla game for a normal world: `chemistry*` is the Education Edition chemistry content, `experimental_y_2026_drop_4` only holds experimental ice/icicle blocks, and the rest hold no block textures. They were left out of the table.

Paths present both loose and inside `__brarchive` in the same pack: 5057 (font 179, materials 18, sounds 4764, texts 30, textures 66). None of them is a block image (0 under `textures/blocks/`). The JSON files used here (`terrain_texture.json`, `flipbook_textures.json`) were checked byte-identical in both places; where an archived copy is an empty placeholder (fonts, materials) the loose copy is read.

Bedrock block names from `j2b` without a `blocks.json` entry (even after the app's upgrade-schema renames): `unknown`

## Counts

| scope | platform | pixels | pixels+model | model | manual | uncertain | total |
|---|---|---|---|---|---|---|---|
| block | java | 1155 | 0 | 1 | 8 | 483 | 1647 |
| block | java_atlas | 67 | 0 | 0 | 0 | 10 | 77 |
| block | bedrock | 1973 | 547 | 53 | 0 | 38 | 2611 |
| item | java | 833 | 0 | 0 | 0 | 339 | 1172 |
| item | bedrock | 1775 | 0 | 0 | 0 | 0 | 1775 |
| all | all | 5803 | 547 | 54 | 8 | 870 | 7282 |

Canonical textures: 4574 (Java 26.1.2 texture 1932, bedrock-only 2594, removed Java texture 48).

### Java 26.1.2 <-> Bedrock

Java block textures: 1112. With at least one Bedrock row: 1087; by strongest evidence: pixels+model 547, pixels 470, model 32, uncertain 38. Without any Bedrock row: 25.

Pair rows: model 53, pixels 481, pixels+model 547, uncertain 38. By method: array 21, carried 11, discovery 37, model 22, older-art 19, other-folder 10, pixels 999.

Bedrock files under `textures/blocks/` (layered, current): 2592, of which 1298 are Vibrant Visuals PBR maps (MER(S)/normal/heightmap, listed as `bedrock:` rows) and 1294 colour textures; 195 colour textures have no Java pair.

## Java name history

| step | gap releases (no jar) | renames | merges | splits | removed (no successor) | added |
|---|---|---|---|---|---|---|
| 1.8.9 -> 1.10.2 | 1.9 ... 1.10.1 (7) | 0 | 0 | 4 | 4 | 43 |
| 1.10.2 -> 1.12.2 | 1.11 ... 1.12.1 (5) | 0 | 0 | 0 | 6 | 69 |
| 1.12.2 -> 1.16.4 | 1.13 ... 1.16.3 (15) | 477 | 1 | 1 | 1 | 216 |
| 1.16.4 -> 1.17.1 | 1.16.5, 1.17 | 2 | 0 | 0 | 0 | 124 |
| 1.17.1 -> 1.18 | - | 0 | 0 | 0 | 0 | 0 |
| 1.18 -> 1.18.1 | - | 0 | 0 | 0 | 0 | 0 |
| 1.18.1 -> 1.18.2 | - | 0 | 0 | 0 | 0 | 0 |
| 1.18.2 -> 1.19 | - | 0 | 0 | 0 | 0 | 40 |
| 1.19 -> 1.19.2 | 1.19.1 | 0 | 0 | 0 | 0 | 0 |
| 1.19.2 -> 1.19.3 | - | 0 | 0 | 0 | 0 | 17 |
| 1.19.3 -> 1.19.4 | - | 0 | 0 | 0 | 0 | 20 |
| 1.19.4 -> 1.20 | - | 0 | 1 | 0 | 0 | 34 |
| 1.20 -> 1.20.1 | - | 0 | 0 | 0 | 0 | 0 |
| 1.20.1 -> 1.21 | 1.20.2 ... 1.20.6 (5) | 1 | 0 | 0 | 0 | 84 |
| 1.21 -> 1.21.1 | - | 0 | 0 | 0 | 0 | 0 |
| 1.21.1 -> 1.21.11 | 1.21.2 ... 1.21.10 (9) | 1 | 0 | 0 | 0 | 99 |
| 1.21.11 -> 26.1.2 | 26.1, 26.1.1 | 0 | 0 | 0 | 0 | 1 |

Renames, merges and splits (excluding the 1.12.2 -> 1.16.4 flattening, listed in the CSV):

- 1.8.9 -> 1.10.2: `blocks/command_block` split `blocks/command_block_conditional` (model; one texture became 4 (block command_block); agrees with Bedrock file blocks/command_block)
- 1.8.9 -> 1.10.2: `blocks/command_block` split `blocks/command_block_front` (model; one texture became 4 (block command_block); agrees with Bedrock file blocks/command_block)
- 1.8.9 -> 1.10.2: `blocks/command_block` split `blocks/command_block_side` (model; one texture became 4 (block command_block); agrees with Bedrock file blocks/command_block)
- 1.8.9 -> 1.10.2: `blocks/command_block` split `blocks/command_block_back` (model; one texture became 4 (block command_block); agrees with Bedrock file blocks/command_block)
- 1.16.4 -> 1.17.1: `block/grass_path_side` rename `block/dirt_path_side` (pixels; identical pixels)
- 1.16.4 -> 1.17.1: `block/grass_path_top` rename `block/dirt_path_top` (pixels; identical pixels)
- 1.19.4 -> 1.20: `block/torchflower_crop_stage2` merge `block/torchflower` (pixels; identical pixels to existing block/torchflower)
- 1.20.1 -> 1.21: `block/grass` rename `block/short_grass` (pixels; identical pixels)
- 1.21.1 -> 1.21.11: `block/chain` rename `block/iron_chain` (pixels; identical pixels)

### 1.12.2 -> 1.16.4 (flattening and 1.14 Texture Update)

Pairs by evidence before the gap rule: model 291, pixels 39, pixels+model 114, uncertain 35. Block links used for model pairing: medium 69, strong 310, weak 12.

Block links come from the app tables: a 1.12.2 recipe file named like the blockstate gives the item id:data, which `flattening.csv` maps to the 1.13 block (strong); a blockstate named like a 1.12 registry id whose data values all flatten to one block (strong); data value 0 of such an id (medium); a blockstate named like a 1.13 block (medium, validated by model structure); name-token similarity (weak, accepted only if a Bedrock file of the same name agrees).

Old textures with no successor after all evidence (1):

- `blocks/flower_paeonia`

Block links that could not be aligned or had conflicting faces (their conflicting pairs were dropped):

- 1.12.2 `anvil` -> 1.16.4 `anvil`: no-align
- 1.12.2 `brown_mushroom_block` -> 1.16.4 `brown_mushroom_block`: no-align
- 1.12.2 `daylight_detector` -> 1.16.4 `daylight_detector`: no-align
- 1.12.2 `daylight_detector_inverted` -> 1.16.4 `daylight_detector`: no-align
- 1.12.2 `double_fern` -> 1.16.4 `potted_fern`: no-align
- 1.12.2 `flower_pot` -> 1.16.4 `flower_pot`: no-align
- 1.12.2 `melon_stem` -> 1.16.4 `melon_stem`: no-align
- 1.12.2 `pumpkin_stem` -> 1.16.4 `pumpkin_stem`: no-align
- 1.12.2 `quartz_double_slab` -> 1.16.4 `quartz_slab`: aligned; blocks/quartz_block_top -> ['block/quartz_block_side', 'block/quartz_block_top']
- 1.12.2 `red_mushroom_block` -> 1.16.4 `red_mushroom_block`: no-align
- 1.12.2 `red_sandstone_double_slab` -> 1.16.4 `red_sandstone_slab`: aligned; blocks/red_sandstone_top -> ['block/red_sandstone', 'block/red_sandstone_bottom', 'block/red_sandstone_top']
- 1.12.2 `sandstone_double_slab` -> 1.16.4 `sandstone_slab`: aligned; blocks/sandstone_top -> ['block/sandstone', 'block/sandstone_bottom', 'block/sandstone_top']
- 1.12.2 `sponge` -> 1.16.4 `sponge`: no-align
- 1.12.2 `stone_double_slab` -> 1.16.4 `smooth_stone_slab`: aligned; blocks/stone_slab_top -> ['block/smooth_stone', 'block/smooth_stone_slab_side']

### Bedrock file-name bridge

Bedrock still uses most pre-1.13 Java file names (e.g. `blocks/planks_oak`). Where an old Java texture has no pixel/model successor, the Java texture paired with the Bedrock file of the same name is proposed. These links are `uncertain` unless they only break a pixel or model tie.

- 1.12.2 -> 1.16.4: `blocks/anvil_base` -> `block/anvil`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/anvil_top_damaged_0` -> `block/anvil_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/anvil_top_damaged_1` -> `block/chipped_anvil_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/anvil_top_damaged_2` -> `block/damaged_anvil_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/cauldron_top` -> `block/cauldron_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_fern_bottom` -> `block/large_fern_bottom`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_fern_top` -> `block/large_fern_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_grass_bottom` -> `block/tall_grass_bottom`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_grass_top` -> `block/tall_grass_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_paeonia_bottom` -> `block/peony_bottom`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_paeonia_top` -> `block/peony_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_syringa_bottom` -> `block/lilac_bottom`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/double_plant_syringa_top` -> `block/lilac_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/flower_houstonia` -> `block/azure_bluet`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/flower_pot` -> `block/flower_pot`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/grass_side` -> `block/grass_block_side_overlay`; already linked to `block/grass_block_side`; reported only
- 1.12.2 -> 1.16.4: `blocks/hopper_inside` -> `block/hopper_inside`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/hopper_top` -> `block/hopper_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/itemframe_background` -> `block/item_frame`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/melon_stem_connected` -> `block/attached_melon_stem`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/melon_stem_disconnected` -> `block/melon_stem`; already linked to nothing; linked (pixels)
- 1.12.2 -> 1.16.4: `blocks/mushroom_block_inside` -> `block/mushroom_block_inside`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/mushroom_block_skin_brown` -> `block/brown_mushroom_block`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/mushroom_block_skin_red` -> `block/red_mushroom_block`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/mushroom_block_skin_stem` -> `block/mushroom_stem`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/pumpkin_stem_connected` -> `block/attached_pumpkin_stem`; already linked to nothing; linked (pixels)
- 1.12.2 -> 1.16.4: `blocks/quartz_block_bottom` -> `block/quartz_block_bottom`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/quartz_block_lines` -> `block/quartz_pillar`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/quartz_block_lines_top` -> `block/quartz_pillar_top`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/shulker_top_purple` -> `block/purple_shulker_box`; already linked to `block/shulker_box`; added split
- 1.12.2 -> 1.16.4: `blocks/sponge` -> `block/sponge`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/sponge_wet` -> `block/wet_sponge`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/stonebrick_cracked` -> `block/cracked_stone_bricks`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/tallgrass` -> `block/grass`; already linked to nothing; linked (uncertain)
- 1.12.2 -> 1.16.4: `blocks/pumpkin_stem_disconnected` -> `block/pumpkin_stem`; already linked to nothing; linked by name (uncertain): pixel twin of blocks/melon_stem_disconnected (identical old files); target picked by file-name tokens, no pixel or model evidence
- 1.12.2 -> 1.16.4: `blocks/water_flow` -> `block/water_flow`; already linked to nothing; linked by name (uncertain): same file name only; no pixel or model evidence (art and models differ)
- 1.12.2 -> 1.16.4: `blocks/water_overlay` -> `block/water_overlay`; already linked to nothing; linked by name (uncertain): same file name only; no pixel or model evidence (art and models differ)
- 1.12.2 -> 1.16.4: `blocks/water_still` -> `block/water_still`; already linked to nothing; linked by name (uncertain): same file name only; no pixel or model evidence (art and models differ)

## Uncertain dates and gaps

Renames between two installed jars with releases in between cannot be dated exactly. Unless a historical fact from the brief pins the date (then `manual` rows cover the gap releases), the row of the old name gets `evidence=uncertain`, the range in its note and the pairing evidence after "pairing evidence:". (Run with --keep-pairing-evidence-in-gaps to keep pixels/model instead.)

| renamed between | rows |
|---|---|
| 1.12.2 and 1.16.4 | 813 |
| 1.20.1 and 1.21 | 1 |
| 1.21.1 and 1.21.11 | 4 |
| 1.8.9 and 1.10.2 | 4 |

Open items:

- 1.5-1.6.4 names: not available (no jar installed). Those releases used older camelCase names; no rows.
- 1.13-1.16.3: no jars. Only 1.12.2 and 1.16.4 names are verified; rows do not claim anything in between (except the `manual` rows for stone_slab_top -> smooth_stone in 1.14).
- 324 Java rows use `from=1.7.2`: 1.7.2-1.8.8 are assumed to match the installed 1.8.9 (Java renamed block textures in 1.7, per the coordinator). Textures whose blocks were added in 1.8 (per the app's `legacy_blocks.csv`) keep `from=1.8.9`.
- Missing jars inside the covered range: 1.2.1, 1.19.1, 1.21.3, 26.1.1, 26.2, 26.3. Renames inside those gaps are dated to a range.
- Java 26.1.2 is the newest installed jar; textures added or renamed in later releases are not covered. Bedrock 1.26.x already ships blocks with no Java 26.1.2 texture (see the Bedrock-only list); some of them may exist in Java 26.2/26.3.
- Pre-1.5 atlas: only b1.0 is installed (1.2.1 has no jar), so `java_atlas` rows are verified for b1.0 only.

## Disagreements

### Java history: pixels vs model

(none: wherever both methods gave an answer they agreed)

### Java <-> Bedrock: pixel twin vs model faces

The pixel twin is kept (primary evidence). If the file Bedrock's blocks.json uses instead is not the pixel twin of another Java texture, it was added as a second `model` row.

- `block/barrel_bottom`: pixels -> `blocks/barrel_bottom`; blocks.json faces -> `blocks/barrel_side` (1 blocks) (blocks: barrel)
- `block/barrel_top`: pixels -> `blocks/barrel_top`; blocks.json faces -> `blocks/barrel_side` (1 blocks) (blocks: barrel)
- `block/chain_command_block_side`: pixels -> `blocks/chain_command_block_side`; blocks.json faces -> `blocks/chain_command_block_conditional_mipmap` (1 blocks) (blocks: chain_command_block)
- `block/command_block_side`: pixels -> `blocks/command_block`, `blocks/command_block_side`; blocks.json faces -> `blocks/command_block_conditional_mipmap` (1 blocks) (blocks: command_block)
- `block/grass_block_side`: pixels -> `blocks/grass_side_carried`; blocks.json faces -> `blocks/grass_side` (1 blocks) (blocks: grass_block)
- `block/mangrove_propagule`: pixels -> `blocks/mangrove_propagule`; blocks.json faces -> `blocks/flower_pot` (1 blocks) (blocks: potted_mangrove_propagule)
- `block/piston_side`: pixels -> `blocks/piston_side`; blocks.json faces -> `blocks/piston_inner` (1 blocks) (blocks: piston_head)
- `block/repeating_command_block_side`: pixels -> `blocks/repeating_command_block_side`; blocks.json faces -> `blocks/repeating_command_block_conditional_mipmap` (1 blocks) (blocks: repeating_command_block)
- `block/twisting_vines`: pixels -> `blocks/twisting_vines_bottom`; blocks.json faces -> `blocks/twisting_vines_base` (1 blocks) (blocks: twisting_vines)
- `block/weeping_vines`: pixels -> `blocks/weeping_vines`; blocks.json faces -> `blocks/weeping_vines_base` (1 blocks) (blocks: weeping_vines)
- `block/weeping_vines_plant`: pixels -> `blocks/weeping_vines_plant`; blocks.json faces -> `blocks/weeping_vines_base` (1 blocks) (blocks: weeping_vines_plant)

### Model pairs rejected

Bedrock uses another Java texture's pixel twin on that face, so the model pairing is not a texture correspondence:

- `block/black_stained_glass_pane_top` -> `blocks/glass_black`: Bedrock file is the pixel twin of Java block/black_stained_glass (blocks: black_stained_glass_pane)
- `block/blue_stained_glass_pane_top` -> `blocks/glass_blue`: Bedrock file is the pixel twin of Java block/blue_stained_glass (blocks: blue_stained_glass_pane)
- `block/brown_stained_glass_pane_top` -> `blocks/glass_brown`: Bedrock file is the pixel twin of Java block/brown_stained_glass (blocks: brown_stained_glass_pane)
- `block/cyan_stained_glass_pane_top` -> `blocks/glass_cyan`: Bedrock file is the pixel twin of Java block/cyan_stained_glass (blocks: cyan_stained_glass_pane)
- `block/dried_kelp_bottom` -> `blocks/dried_kelp_top`: Bedrock file is the pixel twin of Java block/dried_kelp_top (blocks: dried_kelp_block)
- `block/glass_pane_top` -> `blocks/glass`: Bedrock file is the pixel twin of Java block/glass (blocks: glass_pane)
- `block/gray_stained_glass_pane_top` -> `blocks/glass_gray`: Bedrock file is the pixel twin of Java block/gray_stained_glass (blocks: gray_stained_glass_pane)
- `block/green_stained_glass_pane_top` -> `blocks/glass_green`: Bedrock file is the pixel twin of Java block/green_stained_glass (blocks: green_stained_glass_pane)
- `block/light_blue_stained_glass_pane_top` -> `blocks/glass_light_blue`: Bedrock file is the pixel twin of Java block/light_blue_stained_glass (blocks: light_blue_stained_glass_pane)
- `block/light_gray_stained_glass_pane_top` -> `blocks/glass_silver`: Bedrock file is the pixel twin of Java block/light_gray_stained_glass (blocks: light_gray_stained_glass_pane)
- `block/lilac_bottom` -> `blocks/double_plant_sunflower_back`: Bedrock file is the pixel twin of Java block/sunflower_back (blocks: lilac)
- `block/lilac_top` -> `blocks/double_plant_sunflower_back`: Bedrock file is the pixel twin of Java block/sunflower_back (blocks: lilac)
- `block/lime_stained_glass_pane_top` -> `blocks/glass_lime`: Bedrock file is the pixel twin of Java block/lime_stained_glass (blocks: lime_stained_glass_pane)
- `block/magenta_stained_glass_pane_top` -> `blocks/glass_magenta`: Bedrock file is the pixel twin of Java block/magenta_stained_glass (blocks: magenta_stained_glass_pane)
- `block/orange_stained_glass_pane_top` -> `blocks/glass_orange`: Bedrock file is the pixel twin of Java block/orange_stained_glass (blocks: orange_stained_glass_pane)
- `block/pink_stained_glass_pane_top` -> `blocks/glass_pink`: Bedrock file is the pixel twin of Java block/pink_stained_glass (blocks: pink_stained_glass_pane)
- `block/purple_stained_glass_pane_top` -> `blocks/glass_purple`: Bedrock file is the pixel twin of Java block/purple_stained_glass (blocks: purple_stained_glass_pane)
- `block/red_stained_glass_pane_top` -> `blocks/glass_red`: Bedrock file is the pixel twin of Java block/red_stained_glass (blocks: red_stained_glass_pane)
- `block/white_stained_glass_pane_top` -> `blocks/glass_white`: Bedrock file is the pixel twin of Java block/white_stained_glass (blocks: white_stained_glass_pane)
- `block/yellow_stained_glass_pane_top` -> `blocks/glass_yellow`: Bedrock file is the pixel twin of Java block/yellow_stained_glass (blocks: yellow_stained_glass_pane)

### Ambiguous

- bedrock `blocks/double_plant_fern_carried`: {'carried_candidates': ['block/large_fern_top', 'block/large_fern_bottom']}
- bedrock `blocks/double_plant_grass_carried`: {'carried_candidates': ['block/tall_grass_top', 'block/tall_grass_bottom']}
- bedrock `blocks/double_plant_syringa_top`: {'carried_candidates': ['block/lilac_top', 'block/lilac_bottom']}
- bedrock `blocks/seagrass_carried`: {'carried_candidates': ['block/tall_seagrass_top', 'block/tall_seagrass_bottom', 'block/seagrass']}

### Pixel duplicates

- `blocks/hopper_inside`: {'dropped_java': 'block/cauldron_inner', 'kept_java': ['block/hopper_inside']}
- `blocks/pale_moss_block`: {'java': ['block/pale_moss_block', 'block/pale_moss_carpet'], 'kept_java': ['block/pale_moss_block', 'block/pale_moss_carpet']}
- `blocks/sculk_sensor_bottom`: {'dropped_java': 'block/sculk_shrieker_bottom', 'kept_java': ['block/sculk_sensor_bottom']}
- `blocks/vault_bottom`: {'java': ['block/vault_bottom', 'block/vault_bottom_ominous'], 'kept_java': ['block/vault_bottom', 'block/vault_bottom_ominous']}
- `blocks/vault_bottom_ominous`: {'java': ['block/vault_bottom', 'block/vault_bottom_ominous'], 'kept_java': ['block/vault_bottom', 'block/vault_bottom_ominous']}
- `blocks/vault_side_off`: {'java': ['block/vault_side_off', 'block/vault_side_off_ominous'], 'kept_java': ['block/vault_side_off', 'block/vault_side_off_ominous']}
- `blocks/vault_side_off_ominous`: {'java': ['block/vault_side_off', 'block/vault_side_off_ominous'], 'kept_java': ['block/vault_side_off', 'block/vault_side_off_ominous']}

## Unmatched textures

### Java 26.1.2 block textures with no Bedrock row (25)

- `block/bamboo_fence_gate_particle`, `block/bamboo_fence_particle`, `block/bamboo_large_leaves`, `block/bamboo_small_leaves`, `block/conduit`, `block/debug`
- `block/debug2`, `block/dried_kelp_bottom`, `block/kelp`, `block/kelp_plant`, `block/potted_azalea_bush_side`, `block/potted_flowering_azalea_bush_side`
- `block/redstone_dust_dot`, `block/redstone_dust_line0`, `block/redstone_dust_line1`, `block/redstone_dust_overlay`, `block/soul_fire_1`, `block/test_block_accept`
- `block/test_block_fail`, `block/test_block_log`, `block/test_block_start`, `block/test_instance_block`, `block/water_flow`, `block/water_overlay`
- `block/water_still`

### Bedrock colour textures with no Java 26.1.2 pair (195)

Of these, 10 are pixel-identical to an older Java texture and were attached to that texture's canonical (note says which version):

- `blocks/bed_feet_end` = Java `blocks/bed_feet_end` (1.10.2)
- `blocks/bed_feet_side` = Java `blocks/bed_feet_side` (1.10.2)
- `blocks/bed_feet_top` = Java `blocks/bed_feet_top` (1.10.2)
- `blocks/bed_head_end` = Java `blocks/bed_head_end` (1.10.2)
- `blocks/bed_head_side` = Java `blocks/bed_head_side` (1.10.2)
- `blocks/bed_head_top` = Java `blocks/bed_head_top` (1.10.2)
- `blocks/carrots_stage3` = Java `block/carrots_stage3` (26.1.2)
- `blocks/flower_paeonia` = Java `blocks/flower_paeonia` (1.12.2)
- `blocks/pale_moss` = Java `block/pale_moss_carpet` (26.1.2)
- `blocks/soul_fire_1` = Java `block/soul_fire_0` (26.1.2)

29 are pixel twins of Java textures outside `textures/block/` (canonical keeps the Java folder):

- `blocks/angler_pottery_pattern` = Java `entity/decorated_pot/angler_pottery_pattern`
- `blocks/archer_pottery_pattern` = Java `entity/decorated_pot/archer_pottery_pattern`
- `blocks/arms_up_pottery_pattern` = Java `entity/decorated_pot/arms_up_pottery_pattern`
- `blocks/barrier` = Java `item/barrier`
- `blocks/blade_pottery_pattern` = Java `entity/decorated_pot/blade_pottery_pattern`
- `blocks/brewer_pottery_pattern` = Java `entity/decorated_pot/brewer_pottery_pattern`
- `blocks/burn_pottery_pattern` = Java `entity/decorated_pot/burn_pottery_pattern`
- `blocks/conduit_cage` = Java `entity/conduit/cage`
- `blocks/conduit_wind_horizontal` = Java `entity/conduit/wind`
- `blocks/conduit_wind_vertical` = Java `entity/conduit/wind_vertical`
- `blocks/danger_pottery_pattern` = Java `entity/decorated_pot/danger_pottery_pattern`
- `blocks/decorated_pot_base` = Java `entity/decorated_pot/decorated_pot_base`
- `blocks/decorated_pot_side` = Java `entity/decorated_pot/decorated_pot_side`
- `blocks/explorer_pottery_pattern` = Java `entity/decorated_pot/explorer_pottery_pattern`
- `blocks/flow_pottery_pattern` = Java `entity/decorated_pot/flow_pottery_pattern`
- `blocks/friend_pottery_pattern` = Java `entity/decorated_pot/friend_pottery_pattern`
- `blocks/guster_pottery_pattern` = Java `entity/decorated_pot/guster_pottery_pattern`
- `blocks/heart_pottery_pattern` = Java `entity/decorated_pot/heart_pottery_pattern`
- `blocks/heartbreak_pottery_pattern` = Java `entity/decorated_pot/heartbreak_pottery_pattern`
- `blocks/howl_pottery_pattern` = Java `entity/decorated_pot/howl_pottery_pattern`
- `blocks/miner_pottery_pattern` = Java `entity/decorated_pot/miner_pottery_pattern`
- `blocks/mourner_pottery_pattern` = Java `entity/decorated_pot/mourner_pottery_pattern`
- `blocks/plenty_pottery_pattern` = Java `entity/decorated_pot/plenty_pottery_pattern`
- `blocks/prize_pottery_pattern` = Java `entity/decorated_pot/prize_pottery_pattern`
- `blocks/scrape_pottery_pattern` = Java `entity/decorated_pot/scrape_pottery_pattern`
- `blocks/sheaf_pottery_pattern` = Java `entity/decorated_pot/sheaf_pottery_pattern`
- `blocks/shelter_pottery_pattern` = Java `entity/decorated_pot/shelter_pottery_pattern`
- `blocks/skull_pottery_pattern` = Java `entity/decorated_pot/skull_pottery_pattern`
- `blocks/snort_pottery_pattern` = Java `entity/decorated_pot/snort_pottery_pattern`

The remaining 156 are Bedrock-only (`bedrock:` canonical):

- `blocks/azalea_leaves_flowers_opaque`, `blocks/azalea_leaves_opaque`, `blocks/bamboo_leaf`, `blocks/bamboo_small_leaf`, `blocks/border`, `blocks/bubble_column_down_top_a`
- `blocks/bubble_column_down_top_b`, `blocks/bubble_column_down_top_c`, `blocks/bubble_column_down_top_d`, `blocks/bubble_column_inner_a`, `blocks/bubble_column_inner_b`, `blocks/bubble_column_outer_a`
- `blocks/bubble_column_outer_b`, `blocks/bubble_column_outer_c`, `blocks/bubble_column_outer_d`, `blocks/bubble_column_outer_e`, `blocks/bubble_column_outer_f`, `blocks/bubble_column_outer_g`
- `blocks/bubble_column_outer_h`, `blocks/bubble_column_up_top_a`, `blocks/bubble_column_up_top_b`, `blocks/bubble_column_up_top_c`, `blocks/bubble_column_up_top_d`, `blocks/build_allow`
- `blocks/build_deny`, `blocks/camera_back`, `blocks/camera_front`, `blocks/camera_side`, `blocks/camera_top`, `blocks/cauldron_water`
- `blocks/cauldron_water_placeholder`, `blocks/chain1`, `blocks/chain_command_block_back_mipmap`, `blocks/chain_command_block_front_mipmap`, `blocks/chain_command_block_side_mipmap`, `blocks/cherry_leaves_opaque`
- `blocks/chest_front`, `blocks/chest_side`, `blocks/chest_top`, `blocks/chiseled_cinnabar`, `blocks/chiseled_sulfur`, `blocks/cinnabar`
- `blocks/cinnabar_bricks`, `blocks/command_block_back_mipmap`, `blocks/command_block_front_mipmap`, `blocks/command_block_side_mipmap`, `blocks/conduit_base`, `blocks/conduit_closed`
- `blocks/conduit_open`, `blocks/copper_chain1`, `blocks/copper_chest_inventory_front`, `blocks/copper_chest_inventory_side`, `blocks/copper_chest_inventory_top`, `blocks/double_plant_fern_carried`
- `blocks/double_plant_grass_carried`, `blocks/dried_kelp_side_b`, `blocks/end_gateway`, `blocks/end_portal`, `blocks/ender_chest_front`, `blocks/ender_chest_side`
- `blocks/ender_chest_top`, `blocks/exposed_copper_chain1`, `blocks/exposed_copper_chest_inventory_front`, `blocks/exposed_copper_chest_inventory_side`, `blocks/exposed_copper_chest_inventory_top`, `blocks/eyeblossom_blooming`
- `blocks/fire_0_placeholder`, `blocks/fire_1_placeholder`, `blocks/flower_rose_blue`, `blocks/glowing_obsidian`, `blocks/grass_side_snowed`, `blocks/kelp_a`
- `blocks/kelp_b`, `blocks/kelp_c`, `blocks/kelp_d`, `blocks/kelp_top`, `blocks/kelp_top_bulb`, `blocks/lava_placeholder`
- `blocks/leaves_spruce_opaque`, `blocks/mangrove_leaves_opaque`, `blocks/missing_tile`, `blocks/orange_poplar_leaves`, `blocks/orange_poplar_leaves_opaque`, `blocks/oxidized_copper_chain1`
- `blocks/oxidized_copper_chest_inventory_front`, `blocks/oxidized_copper_chest_inventory_side`, `blocks/oxidized_copper_chest_inventory_top`, `blocks/pale_oak_leaves_opaque`, `blocks/polished_cinnabar`, `blocks/polished_sulfur`
- `blocks/poplar_door_bottom`, `blocks/poplar_door_top`, `blocks/poplar_log_side`, `blocks/poplar_log_top`, `blocks/poplar_planks`, `blocks/poplar_sapling`
- `blocks/poplar_shelf`, `blocks/poplar_trapdoor`, `blocks/portal_placeholder`, `blocks/potent_sulfur`, `blocks/potted_azalea_bush_side`, `blocks/potted_flowering_azalea_bush_side`
- `blocks/reactor_core_stage_0`, `blocks/reactor_core_stage_1`, `blocks/reactor_core_stage_2`, `blocks/red_poplar_leaves`, `blocks/red_poplar_leaves_opaque`, `blocks/red_shrub`
- `blocks/redstone_dust_cross`, `blocks/redstone_dust_line`, `blocks/repeating_command_block_back_mipmap`, `blocks/repeating_command_block_front_mipmap`, `blocks/repeating_command_block_side_mipmap`, `blocks/seagrass_carried`
- `blocks/seagrass_doubletall_bottom_b`, `blocks/seagrass_doubletall_top_b`, `blocks/shelf_mushroom_large`, `blocks/shelf_mushroom_small`, `blocks/stonecutter_side`, `blocks/stonecutter_top`
- `blocks/straw_bed`, `blocks/stripped_poplar_log_side`, `blocks/stripped_poplar_log_top`, `blocks/stripped_poplar_side_log`, `blocks/structure_air`, `blocks/structure_block_export`
- `blocks/structure_void`, `blocks/sulfur`, `blocks/sulfur_bricks`, `blocks/sulfur_spike_down_base`, `blocks/sulfur_spike_down_frustum`, `blocks/sulfur_spike_down_middle`
- `blocks/sulfur_spike_down_tip`, `blocks/sulfur_spike_down_tip_merge`, `blocks/sulfur_spike_up_base`, `blocks/sulfur_spike_up_frustum`, `blocks/sulfur_spike_up_middle`, `blocks/sulfur_spike_up_tip`
- `blocks/sulfur_spike_up_tip_merge`, `blocks/trapped_chest_front`, `blocks/trial_spawner_side`, `blocks/trial_spawner_top`, `blocks/trial_spawner_top_open`, `blocks/vine_carried`
- `blocks/water_flow`, `blocks/water_flow_grey`, `blocks/water_placeholder`, `blocks/water_still`, `blocks/water_still_grey`, `blocks/weathered_copper_chain1`
- `blocks/weathered_copper_chest_inventory_front`, `blocks/weathered_copper_chest_inventory_side`, `blocks/weathered_copper_chest_inventory_top`, `blocks/weeping_vines_bottom`, `blocks/yellow_poplar_leaves`, `blocks/yellow_poplar_leaves_opaque`

Pairs found only by comparing leftover images (no model link; `uncertain`):

- `block/beehive_front` <-> `blocks/beehive_front`: no model link; found by comparing leftover images of the same size; partial tint: 71 of 256 visible pixels differ by one tint x#efede4, the rest identical
- `block/beehive_front_honey` <-> `blocks/beehive_front_honey`: no model link; found by comparing leftover images of the same size; partial tint: 54 of 256 visible pixels differ by one tint x#f0efe6, the rest identical
- `block/black_stained_glass_pane_top` <-> `blocks/glass_pane_top_black`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/blue_stained_glass_pane_top` <-> `blocks/glass_pane_top_blue`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/brown_stained_glass_pane_top` <-> `blocks/glass_pane_top_brown`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/cyan_stained_glass_pane_top` <-> `blocks/glass_pane_top_cyan`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_0_east` <-> `blocks/dried_ghast_state_1_right`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_0_north` <-> `blocks/dried_ghast_state_1_front`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_0_south` <-> `blocks/dried_ghast_state_1_back`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_0_west` <-> `blocks/dried_ghast_state_1_left`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_1_east` <-> `blocks/dried_ghast_state_2_right`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_1_north` <-> `blocks/dried_ghast_state_2_front`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_1_south` <-> `blocks/dried_ghast_state_2_back`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_1_west` <-> `blocks/dried_ghast_state_2_left`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_2_east` <-> `blocks/dried_ghast_state_3_right`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_2_north` <-> `blocks/dried_ghast_state_3_front`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_2_south` <-> `blocks/dried_ghast_state_3_back`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_2_west` <-> `blocks/dried_ghast_state_3_left`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_3_east` <-> `blocks/dried_ghast_state_4_right`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_3_north` <-> `blocks/dried_ghast_state_4_front`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_3_south` <-> `blocks/dried_ghast_state_4_back`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/dried_ghast_hydration_3_west` <-> `blocks/dried_ghast_state_4_left`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/glass_pane_top` <-> `blocks/glass_pane_top`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/gray_stained_glass_pane_top` <-> `blocks/glass_pane_top_gray`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/green_stained_glass_pane_top` <-> `blocks/glass_pane_top_green`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/light_blue_stained_glass_pane_top` <-> `blocks/glass_pane_top_light_blue`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/light_gray_stained_glass_pane_top` <-> `blocks/glass_pane_top_silver`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/lilac_bottom` <-> `blocks/double_plant_syringa_bottom`: no model link; found by comparing leftover images of the same size; near-identical colours (within 4 levels)
- `block/lilac_top` <-> `blocks/double_plant_syringa_top`: no model link; found by comparing leftover images of the same size; near-identical colours (within 4 levels)
- `block/lime_stained_glass_pane_top` <-> `blocks/glass_pane_top_lime`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/magenta_stained_glass_pane_top` <-> `blocks/glass_pane_top_magenta`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/orange_stained_glass_pane_top` <-> `blocks/glass_pane_top_orange`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/pink_stained_glass_pane_top` <-> `blocks/glass_pane_top_pink`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/purple_stained_glass_pane_top` <-> `blocks/glass_pane_top_purple`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/red_stained_glass_pane_top` <-> `blocks/glass_pane_top_red`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/white_stained_glass_pane_top` <-> `blocks/glass_pane_top_white`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs
- `block/yellow_stained_glass_pane_top` <-> `blocks/glass_pane_top_yellow`: no model link; found by comparing leftover images of the same size; same colours where both are visible; only transparency differs

## Tint, size and animation differences

Pairs whose images differ only by tint, or by a tint on part of the image (alpha masks compared first, then one factor per channel fitted over pixels visible in both, max error 4 levels; luminance correlation is reported for the rest). "Java grey (tinted in game)" means Java stores a grey texture and colours it by biome or block; Bedrock ships it pre-coloured:

- `beehive_front` <-> `blocks/beehive_front` (uncertain): partial tint: 71 of 256 visible pixels differ by one tint x#efede4, the rest identical
- `beehive_front_honey` <-> `blocks/beehive_front_honey` (uncertain): partial tint: 54 of 256 visible pixels differ by one tint x#f0efe6, the rest identical
- `exposed_copper_chain` <-> `blocks/exposed_copper_chain2` (model): partial tint: 1 of 26 visible pixels differ by one tint x#ffffff, the rest identical
- `fern` <-> `blocks/fern_carried` (model): differs only by tint: Bedrock = Java x#7abc69
- `fire_coral_block` <-> `blocks/coral_red` (model): partial tint: 13 of 256 visible pixels differ by one tint x#edffff, the rest identical
- `grass_block_top` <-> `blocks/grass_carried` (model): differs only by tint: Java grey (tinted in game), Bedrock pre-coloured x#87cd49

Size differences (11):

- `chain_command_block_side` <-> `blocks/chain_command_block_conditional_mipmap` (model): size differs: Java 16x64, Bedrock 16x16
- `command_block_back` <-> `blocks/command_block` (pixels): size differs: Java 16x64, Bedrock 16x16
- `command_block_conditional` <-> `blocks/command_block` (pixels): size differs: Java 16x64, Bedrock 16x16
- `command_block_front` <-> `blocks/command_block` (pixels): size differs: Java 16x64, Bedrock 16x16
- `command_block_side` <-> `blocks/command_block` (pixels): size differs: Java 16x64, Bedrock 16x16
- `command_block_side` <-> `blocks/command_block_conditional_mipmap` (model): size differs: Java 16x64, Bedrock 16x16
- `item/compass_00` <-> `items/compass_atlas` (pixels): size differs: Java 16x16, Bedrock 16x512
- `item/compass_00` <-> `items/lodestonecompass_atlas` (pixels): size differs: Java 16x16, Bedrock 16x512
- `item/recovery_compass_00` <-> `items/recovery_compass_atlas` (pixels): size differs: Java 16x16, Bedrock 16x512
- `lava_still` <-> `blocks/lava_still` (pixels): size differs: Java 16x320, Bedrock 16x512
- `repeating_command_block_side` <-> `blocks/repeating_command_block_conditional_mipmap` (model): size differs: Java 16x64, Bedrock 16x16

Animated textures matched by frames rather than whole strips:

- `item/compass_00` <-> `items/compass_atlas`: pixels: first frame
- `item/compass_00` <-> `items/lodestonecompass_atlas`: pixels: first frame
- `item/recovery_compass_00` <-> `items/recovery_compass_atlas`: pixels: first frame
- `lava_still` <-> `blocks/lava_still`: pixels: first frame

## Pre-1.5 atlas (`terrain.png`)

b1.0 `terrain.png` is 256x256 (16x16 tiles of 16x16). Matched by pixels against 1.8.9 `blocks/` textures (whole image or any animation frame).

Identical tiles: 63 (`pixels` rows). Unmatched tiles: 193, of which 10 have one clear partial match (at least 80% of visible pixels identical, next best at least 10 points lower; `uncertain` rows) and 136 hold only one or two colours (unused or placeholder slots). Fully transparent tiles: 0.

Identical (index = y*16+x):

- 5 (x=5, y=0) = `blocks/stone_slab_side`
- 6 (x=6, y=0) = `blocks/stone_slab_top`
- 8 (x=8, y=0) = `blocks/tnt_side`
- 9 (x=9, y=0) = `blocks/tnt_top`
- 10 (x=10, y=0) = `blocks/tnt_bottom`
- 13 (x=13, y=0) = `blocks/flower_dandelion`
- 17 (x=1, y=1) = `blocks/bedrock`
- 20 (x=4, y=1) = `blocks/log_oak`
- 21 (x=5, y=1) = `blocks/log_oak_top`
- 28 (x=12, y=1) = `blocks/mushroom_red`
- 29 (x=13, y=1) = `blocks/mushroom_brown`
- 32 (x=0, y=2) = `blocks/gold_ore`
- 33 (x=1, y=2) = `blocks/iron_ore`
- 34 (x=2, y=2) = `blocks/coal_ore`
- 35 (x=3, y=2) = `blocks/bookshelf`
- 37 (x=5, y=2) = `blocks/obsidian`
- 43 (x=11, y=2) = `blocks/crafting_table_top`
- 44 (x=12, y=2) = `blocks/furnace_front_off`
- 45 (x=13, y=2) = `blocks/furnace_side`
- 49 (x=1, y=3) = `blocks/glass`
- 50 (x=2, y=3) = `blocks/diamond_ore`
- 51 (x=3, y=3) = `blocks/redstone_ore`
- 52 (x=4, y=3) = `blocks/leaves_acacia`, `blocks/leaves_big_oak`, `blocks/leaves_birch`, `blocks/leaves_oak`
- 59 (x=11, y=3) = `blocks/crafting_table_side`
- 60 (x=12, y=3) = `blocks/crafting_table_front`
- 61 (x=13, y=3) = `blocks/furnace_front_on`
- 64 (x=0, y=4) = `blocks/wool_colored_white`
- 66 (x=2, y=4) = `blocks/snow`
- 67 (x=3, y=4) = `blocks/ice`
- 68 (x=4, y=4) = `blocks/grass_side_snowed`
- 69 (x=5, y=4) = `blocks/cactus_top`
- 70 (x=6, y=4) = `blocks/cactus_side`
- 71 (x=7, y=4) = `blocks/cactus_bottom`
- 72 (x=8, y=4) = `blocks/clay`
- 73 (x=9, y=4) = `blocks/reeds`
- 74 (x=10, y=4) = `blocks/jukebox_side`, `blocks/noteblock`
- 75 (x=11, y=4) = `blocks/jukebox_top`
- 80 (x=0, y=5) = `blocks/torch_on`
- 81 (x=1, y=5) = `blocks/door_wood_upper`
- 82 (x=2, y=5) = `blocks/door_iron_upper`
- 83 (x=3, y=5) = `blocks/ladder`
- 86 (x=6, y=5) = `blocks/farmland_wet`
- 87 (x=7, y=5) = `blocks/farmland_dry`
- 88 (x=8, y=5) = `blocks/wheat_stage_0`
- 89 (x=9, y=5) = `blocks/wheat_stage_1`
- 90 (x=10, y=5) = `blocks/wheat_stage_2`
- 91 (x=11, y=5) = `blocks/wheat_stage_3`
- 92 (x=12, y=5) = `blocks/wheat_stage_4`
- 93 (x=13, y=5) = `blocks/wheat_stage_5`
- 94 (x=14, y=5) = `blocks/wheat_stage_6`
- 95 (x=15, y=5) = `blocks/wheat_stage_7`
- 96 (x=0, y=6) = `blocks/lever`
- 97 (x=1, y=6) = `blocks/door_wood_lower`
- 98 (x=2, y=6) = `blocks/door_iron_lower`
- 99 (x=3, y=6) = `blocks/redstone_torch_on`
- 102 (x=6, y=6) = `blocks/pumpkin_top`
- 104 (x=8, y=6) = `blocks/soul_sand`
- 112 (x=0, y=7) = `blocks/rail_normal_turned`
- 115 (x=3, y=7) = `blocks/redstone_torch_off`
- 118 (x=6, y=7) = `blocks/pumpkin_side`
- 119 (x=7, y=7) = `blocks/pumpkin_face_off`
- 120 (x=8, y=7) = `blocks/pumpkin_face_on`
- 128 (x=0, y=8) = `blocks/rail_normal`

Partial matches written as `uncertain` rows:

- 0 (x=0, y=0) ~ `blocks/grass_top` (91% of visible pixels identical)
- 1 (x=1, y=0) ~ `blocks/stone` (98% of visible pixels identical)
- 2 (x=2, y=0) ~ `blocks/dirt` (96% of visible pixels identical)
- 3 (x=3, y=0) ~ `blocks/grass_side` (96% of visible pixels identical)
- 4 (x=4, y=0) ~ `blocks/planks_oak` (98% of visible pixels identical)
- 18 (x=2, y=1) ~ `blocks/sand` (89% of visible pixels identical)
- 23 (x=7, y=1) ~ `blocks/gold_block` (89% of visible pixels identical)
- 24 (x=8, y=1) ~ `blocks/diamond_block` (96% of visible pixels identical)
- 103 (x=7, y=6) ~ `blocks/netherrack` (97% of visible pixels identical)
- 105 (x=9, y=6) ~ `blocks/glowstone` (84% of visible pixels identical)

Unmatched, no row (best overlap shown for orientation only):

- 7 (x=7, y=0): `blocks/door_iron_upper` 2%, `blocks/wool_colored_white` 1%
- 12 (x=12, y=0): `blocks/flower_rose` 33%, `blocks/flower_dandelion` 7%
- 15 (x=15, y=0): `blocks/deadbush` 37%, `blocks/carrots_stage_3` 3%
- 16 (x=0, y=1): `blocks/stone` 4%, `blocks/redstone_ore` 3%
- 19 (x=3, y=1): `blocks/waterlily` 0%, `blocks/rail_normal` 0%
- 22 (x=6, y=1): `blocks/iron_block` 15%, `blocks/wool_colored_white` 3%
- 25 (x=9, y=1): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 26 (x=10, y=1): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 27 (x=11, y=1): `blocks/tallgrass` 0%, `blocks/wool_colored_yellow` 0%
- 36 (x=4, y=2): `blocks/cobblestone_mossy` 3%, `blocks/stone` 3%
- 38 (x=6, y=2): `blocks/repeater_on` 1%, `blocks/repeater_off` 1%
- 39 (x=7, y=2): `blocks/gold_block` 0%, `blocks/wool_colored_yellow` 0%
- 40 (x=8, y=2): `blocks/diamond_block` 0%, `blocks/wool_colored_yellow` 0%
- 41 (x=9, y=2): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 42 (x=10, y=2): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 48 (x=0, y=3): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 53 (x=5, y=3): `blocks/leaves_oak` 61%, `blocks/leaves_birch` 61%
- 54 (x=6, y=3): `blocks/stone_slab_side` 2%, `blocks/door_iron_lower` 2%
- 55 (x=7, y=3): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 56 (x=8, y=3): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 57 (x=9, y=3): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 58 (x=10, y=3): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 65 (x=1, y=4): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 84 (x=4, y=5): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 85 (x=5, y=5): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 100 (x=4, y=6): `blocks/redstone_torch_on` 5%, `blocks/wool_colored_yellow` 0%
- 101 (x=5, y=6): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 205 (x=13, y=12): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 206 (x=14, y=12): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 207 (x=15, y=12): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 222 (x=14, y=13): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 223 (x=15, y=13): `blocks/wool_colored_yellow` 0%, `blocks/wool_colored_white` 0%
- 237 (x=13, y=14): `blocks/furnace_front_on` 4%, `blocks/wool_colored_yellow` 0%
- 238 (x=14, y=14): `blocks/furnace_front_on` 4%, `blocks/wool_colored_yellow` 0%
- 239 (x=15, y=14): `blocks/furnace_front_on` 4%, `blocks/wool_colored_yellow` 0%
- 240 (x=0, y=15): `blocks/stone` 32%, `blocks/redstone_ore` 27%
- 241 (x=1, y=15): `blocks/stone` 32%, `blocks/redstone_ore` 27%
- 242 (x=2, y=15): `blocks/stone` 30%, `blocks/redstone_ore` 27%
- 243 (x=3, y=15): `blocks/stone` 30%, `blocks/redstone_ore` 26%
- 244 (x=4, y=15): `blocks/stone` 29%, `blocks/redstone_ore` 26%
- 245 (x=5, y=15): `blocks/stone` 27%, `blocks/redstone_ore` 24%
- 246 (x=6, y=15): `blocks/destroy_stage_6` 27%, `blocks/stone` 27%
- 247 (x=7, y=15): `blocks/destroy_stage_7` 39%, `blocks/destroy_stage_9` 38%
- 248 (x=8, y=15): `blocks/destroy_stage_8` 50%, `blocks/destroy_stage_9` 50%
- 249 (x=9, y=15): `blocks/destroy_stage_9` 57%, `blocks/destroy_stage_8` 50%
- 254 (x=14, y=15): `blocks/furnace_front_on` 4%, `blocks/wool_colored_yellow` 0%
- 255 (x=15, y=15): `blocks/furnace_front_on` 4%, `blocks/wool_colored_yellow` 0%

One- or two-colour tiles (no texture): 11, 14, 30, 31, 46, 47, 62, 63, 76, 77, 78, 79, 106, 107, 108, 109, 110, 111, 113, 114, 116, 117, 121, 122, 123, 124, 125, 126, 127, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150, 151, 152, 153, 154, 155, 156, 157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 250, 251, 252, 253

Fully transparent tiles: (none)

## Items (Part 4, pixel identity only)

Item textures (`textures/item(s)/`, Bedrock `textures/items/`) were paired by pixel identity only: no item model pairing was done. History links across 1.12.2 -> 1.16.4 without identical pixels come from the Bedrock file of the same name or, last, from the same file name; both are `uncertain`. Canonical: `item/<26.1.2 name>`.

Java item textures per installed release: 1.8.9 227, 1.10.2 340, 1.12.2 343, 1.16.4 408, 1.17.1 456, 1.18 457, 1.18.1 457, 1.18.2 457, 1.19 505, 1.19.2 505, 1.19.3 519, 1.19.4 557, 1.20 582, 1.20.1 582, 1.21 605, 1.21.1 605, 1.21.11 792, 26.1.2 792. Bedrock `textures/items/` files (layered): 1766.

| step | renames | merges | splits | removed | added |
|---|---|---|---|---|---|
| 1.8.9 -> 1.10.2 | 0 | 0 | 0 | 4 | 117 |
| 1.10.2 -> 1.12.2 | 0 | 0 | 0 | 2 | 5 |
| 1.12.2 -> 1.16.4 | 335 | 0 | 0 | 9 | 73 |
| 1.16.4 -> 1.17.1 | 0 | 0 | 0 | 1 | 49 |
| 1.17.1 -> 1.18 | 0 | 0 | 0 | 0 | 1 |
| 1.18 -> 1.18.1 | 0 | 0 | 0 | 0 | 0 |
| 1.18.1 -> 1.18.2 | 0 | 0 | 0 | 0 | 0 |
| 1.18.2 -> 1.19 | 0 | 0 | 0 | 0 | 48 |
| 1.19 -> 1.19.2 | 0 | 0 | 0 | 0 | 0 |
| 1.19.2 -> 1.19.3 | 0 | 0 | 0 | 0 | 14 |
| 1.19.3 -> 1.19.4 | 0 | 0 | 0 | 0 | 38 |
| 1.19.4 -> 1.20 | 4 | 0 | 0 | 0 | 25 |
| 1.20 -> 1.20.1 | 0 | 0 | 0 | 0 | 0 |
| 1.20.1 -> 1.21 | 1 | 0 | 0 | 0 | 23 |
| 1.21 -> 1.21.1 | 0 | 0 | 0 | 0 | 0 |
| 1.21.1 -> 1.21.11 | 2 | 1 | 0 | 21 | 209 |
| 1.21.11 -> 26.1.2 | 0 | 0 | 0 | 0 | 0 |

Item history links by evidence: pixels 142, uncertain 201.

- 1.8.9 -> 1.10.2, item textures with no successor: `items/boat`, `items/clock`, `items/compass`, `items/quiver`
- 1.10.2 -> 1.12.2, item textures with no successor: `items/banner_base`, `items/bed`
- 1.12.2 -> 1.16.4, item textures with no successor: `items/banner_overlay`, `items/fish_cod_cooked`, `items/fish_cod_raw`, `items/map_empty`, `items/map_filled`, `items/potion_bottle_drinkable`, `items/potion_bottle_lingering`, `items/potion_bottle_splash`, `items/wooden_armorstand`
- 1.16.4 -> 1.17.1, item textures with no successor: `item/ruby`
- 1.21.1 -> 1.21.11, item textures with no successor: `item/empty_armor_slot_boots`, `item/empty_armor_slot_chestplate`, `item/empty_armor_slot_helmet`, `item/empty_armor_slot_leggings`, `item/empty_armor_slot_shield`, `item/empty_slot_amethyst_shard`, `item/empty_slot_axe`, `item/empty_slot_diamond`, `item/empty_slot_emerald`, `item/empty_slot_hoe`, `item/empty_slot_ingot`, `item/empty_slot_lapis_lazuli`, `item/empty_slot_pickaxe`, `item/empty_slot_quartz`, `item/empty_slot_redstone_dust`, `item/empty_slot_shovel`, `item/empty_slot_smithing_template_armor_trim`, `item/empty_slot_smithing_template_netherite_upgrade`, `item/empty_slot_sword`, `item/spawn_egg`, `item/spawn_egg_overlay`

Java 26.1.2 item textures: 792; with a Bedrock pixel twin: 614; without: 178.

Java item textures without a Bedrock pixel twin:

- `item/clock_01`, `item/clock_02`, `item/clock_03`, `item/clock_04`, `item/clock_05`, `item/clock_06`
- `item/clock_07`, `item/clock_08`, `item/clock_09`, `item/clock_10`, `item/clock_11`, `item/clock_12`
- `item/clock_13`, `item/clock_14`, `item/clock_15`, `item/clock_16`, `item/clock_17`, `item/clock_18`
- `item/clock_19`, `item/clock_20`, `item/clock_21`, `item/clock_22`, `item/clock_23`, `item/clock_24`
- `item/clock_25`, `item/clock_26`, `item/clock_27`, `item/clock_28`, `item/clock_29`, `item/clock_30`
- `item/clock_31`, `item/clock_32`, `item/clock_33`, `item/clock_34`, `item/clock_35`, `item/clock_36`
- `item/clock_37`, `item/clock_38`, `item/clock_39`, `item/clock_40`, `item/clock_41`, `item/clock_42`
- `item/clock_43`, `item/clock_44`, `item/clock_45`, `item/clock_46`, `item/clock_47`, `item/clock_48`
- `item/clock_49`, `item/clock_50`, `item/clock_51`, `item/clock_52`, `item/clock_53`, `item/clock_54`
- `item/clock_55`, `item/clock_56`, `item/clock_57`, `item/clock_58`, `item/clock_59`, `item/clock_60`
- `item/clock_61`, `item/clock_62`, `item/clock_63`, `item/compass_01`, `item/compass_02`, `item/compass_03`
- `item/compass_04`, `item/compass_05`, `item/compass_06`, `item/compass_07`, `item/compass_08`, `item/compass_09`
- `item/compass_10`, `item/compass_11`, `item/compass_12`, `item/compass_13`, `item/compass_14`, `item/compass_15`
- `item/compass_16`, `item/compass_17`, `item/compass_18`, `item/compass_20`, `item/compass_21`, `item/compass_22`
- `item/compass_23`, `item/compass_24`, `item/compass_25`, `item/compass_26`, `item/compass_27`, `item/compass_28`
- `item/compass_29`, `item/compass_30`, `item/compass_31`, `item/copper_spear_in_hand`, `item/diamond_spear_in_hand`, `item/filled_map`
- `item/filled_map_markings`, `item/firefly_bush`, `item/firework_star`, `item/firework_star_overlay`, `item/golden_spear_in_hand`, `item/honeycomb`
- `item/iron_horse_armor`, `item/iron_spear_in_hand`, `item/knowledge_book`, `item/leather_boots`, `item/leather_boots_overlay`, `item/leather_chestplate_overlay`
- `item/leather_helmet`, `item/leather_helmet_overlay`, `item/leather_horse_armor`, `item/leather_horse_armor_overlay`, `item/leather_leggings`, `item/leather_leggings_overlay`
- `item/light`, `item/light_00`, `item/light_01`, `item/light_02`, `item/light_03`, `item/light_04`
- `item/light_05`, `item/light_06`, `item/light_07`, `item/light_08`, `item/light_09`, `item/light_10`
- `item/light_11`, `item/light_12`, `item/light_13`, `item/light_14`, `item/light_15`, `item/map`
- `item/mooshroom_spawn_egg`, `item/netherite_spear_in_hand`, `item/pale_oak_boat`, `item/pale_oak_chest_boat`, `item/pitcher_plant`, `item/pointed_dripstone`
- `item/potion_overlay`, `item/recovery_compass_01`, `item/recovery_compass_02`, `item/recovery_compass_03`, `item/recovery_compass_04`, `item/recovery_compass_05`
- `item/recovery_compass_06`, `item/recovery_compass_07`, `item/recovery_compass_08`, `item/recovery_compass_09`, `item/recovery_compass_10`, `item/recovery_compass_11`
- `item/recovery_compass_12`, `item/recovery_compass_13`, `item/recovery_compass_14`, `item/recovery_compass_15`, `item/recovery_compass_16`, `item/recovery_compass_17`
- `item/recovery_compass_18`, `item/recovery_compass_20`, `item/recovery_compass_21`, `item/recovery_compass_22`, `item/recovery_compass_23`, `item/recovery_compass_24`
- `item/recovery_compass_25`, `item/recovery_compass_26`, `item/recovery_compass_27`, `item/recovery_compass_28`, `item/recovery_compass_29`, `item/recovery_compass_30`
- `item/recovery_compass_31`, `item/seagrass`, `item/spectral_arrow`, `item/splash_potion`, `item/spyglass_model`, `item/stone_spear_in_hand`
- `item/structure_void`, `item/wildflowers`, `item/wolf_armor_overlay`, `item/wooden_spear_in_hand`

Bedrock item files without a Java pixel twin (1140):

- `items/acacia_chest_boat_mers`, `items/acacia_hanging_sign_mers`, `items/amethyst_shard_mers`, `items/angler_pottery_sherd_mers`, `items/apple_golden_mers`, `items/apple_mers`
- `items/archer_pottery_sherd_mers`, `items/armadillo_scute_mers`, `items/armor_stand_mers`, `items/arms_up_pottery_sherd_mers`, `items/arrow_mers`, `items/bamboo_chest_raft_mers`
- `items/bamboo_door_mers`, `items/bamboo_hanging_sign_mers`, `items/bamboo_mers`, `items/bamboo_raft_mers`, `items/bamboo_sign_mers`, `items/banner_pattern_mers`
- `items/bed_black`, `items/bed_black_mers`, `items/bed_blue`, `items/bed_blue_mers`, `items/bed_brown`, `items/bed_brown_mers`
- `items/bed_cyan`, `items/bed_cyan_mers`, `items/bed_gray`, `items/bed_gray_mers`, `items/bed_green`, `items/bed_green_mers`
- `items/bed_light_blue`, `items/bed_light_blue_mers`, `items/bed_lime`, `items/bed_lime_mers`, `items/bed_magenta`, `items/bed_magenta_mers`
- `items/bed_orange`, `items/bed_orange_mers`, `items/bed_pink`, `items/bed_pink_mers`, `items/bed_purple`, `items/bed_purple_mers`
- `items/bed_red`, `items/bed_red_mers`, `items/bed_silver`, `items/bed_silver_mers`, `items/bed_white`, `items/bed_white_mers`
- `items/bed_yellow`, `items/bed_yellow_mers`, `items/beef_cooked_mers`, `items/beef_raw_mers`, `items/beetroot_mers`, `items/beetroot_soup_mers`
- `items/birch_chest_boat_mers`, `items/birch_hanging_sign_mers`, `items/black_cushion`, `items/black_cushion_mers`, `items/blade_pottery_sherd_mers`, `items/blaze_powder_mers`
- `items/blaze_rod_mers`, `items/blue_cushion`, `items/blue_cushion_mers`, `items/blue_egg_mers`, `items/boat_acacia_mers`, `items/boat_birch_mers`
- `items/boat_dark_oak`, `items/boat_darkoak_mers`, `items/boat_jungle_mers`, `items/boat_mers`, `items/boat_oak_mers`, `items/boat_spruce_mers`
- `items/bolt_armor_trim_smithing_template_mers`, `items/bone_mers`, `items/book_enchanted_mers`, `items/book_normal_mers`, `items/book_portfolio`, `items/book_portfolio_mers`
- `items/book_writable_mers`, `items/book_written_mers`, `items/bordure_indented_banner_pattern_mers`, `items/bow_pulling_0_mers`, `items/bow_pulling_1_mers`, `items/bow_pulling_2_mers`
- `items/bow_standby_mers`, `items/bowl_mers`, `items/bread_mers`, `items/breeze_rod_mers`, `items/brewer_pottery_sherd_mers`, `items/brewing_stand_mers`
- `items/brick_mers`, `items/broken_elytra_mers`, `items/brown_cushion`, `items/brown_cushion_mers`, `items/brown_egg_mers`, `items/brush_mers`
- `items/bucket_axolotl_mers`, `items/bucket_cod_mers`, `items/bucket_empty_mers`, `items/bucket_lava_mers`, `items/bucket_milk_mers`, `items/bucket_powder_snow_mers`
- `items/bucket_pufferfish_mers`, `items/bucket_salmon_mers`, `items/bucket_sulfur_cube`, `items/bucket_sulfur_cube_mers`, `items/bucket_tadpole_mers`, `items/bucket_tropical_mers`
- `items/bucket_water_mers`, `items/bundle_black_mers`, `items/bundle_black_open`, `items/bundle_black_open_back_mers`, `items/bundle_black_open_front_mers`, `items/bundle_black_open_mers`
- `items/bundle_blue_mers`, `items/bundle_blue_open`, `items/bundle_blue_open_back_mers`, `items/bundle_blue_open_front_mers`, `items/bundle_blue_open_mers`, `items/bundle_brown_mers`
- `items/bundle_brown_open`, `items/bundle_brown_open_back_mers`, `items/bundle_brown_open_front_mers`, `items/bundle_brown_open_mers`, `items/bundle_cyan_mers`, `items/bundle_cyan_open`
- `items/bundle_cyan_open_back_mers`, `items/bundle_cyan_open_front_mers`, `items/bundle_cyan_open_mers`, `items/bundle_gray_mers`, `items/bundle_gray_open`, `items/bundle_gray_open_back_mers`
- `items/bundle_gray_open_front_mers`, `items/bundle_gray_open_mers`, `items/bundle_green_mers`, `items/bundle_green_open`, `items/bundle_green_open_back_mers`, `items/bundle_green_open_front_mers`
- `items/bundle_green_open_mers`, `items/bundle_light_blue_mers`, `items/bundle_light_blue_open`, `items/bundle_light_blue_open_back_mers`, `items/bundle_light_blue_open_front_mers`, `items/bundle_light_blue_open_mers`
- `items/bundle_light_gray_mers`, `items/bundle_light_gray_open`, `items/bundle_light_gray_open_back_mers`, `items/bundle_light_gray_open_front_mers`, `items/bundle_light_gray_open_mers`, `items/bundle_lime_mers`
- `items/bundle_lime_open`, `items/bundle_lime_open_back_mers`, `items/bundle_lime_open_front_mers`, `items/bundle_lime_open_mers`, `items/bundle_magenta_mers`, `items/bundle_magenta_open`
- `items/bundle_magenta_open_back_mers`, `items/bundle_magenta_open_front_mers`, `items/bundle_magenta_open_mers`, `items/bundle_mers`, `items/bundle_open`, `items/bundle_open_back_mers`
- `items/bundle_open_front_mers`, `items/bundle_open_mers`, `items/bundle_orange_mers`, `items/bundle_orange_open`, `items/bundle_orange_open_back_mers`, `items/bundle_orange_open_front_mers`
- `items/bundle_orange_open_mers`, `items/bundle_pink_mers`, `items/bundle_pink_open`, `items/bundle_pink_open_back_mers`, `items/bundle_pink_open_front_mers`, `items/bundle_pink_open_mers`
- `items/bundle_purple_mers`, `items/bundle_purple_open`, `items/bundle_purple_open_back_mers`, `items/bundle_purple_open_front_mers`, `items/bundle_purple_open_mers`, `items/bundle_red_mers`
- `items/bundle_red_open`, `items/bundle_red_open_back_mers`, `items/bundle_red_open_front_mers`, `items/bundle_red_open_mers`, `items/bundle_white_mers`, `items/bundle_white_open`
- `items/bundle_white_open_back_mers`, `items/bundle_white_open_front_mers`, `items/bundle_white_open_mers`, `items/bundle_yellow_mers`, `items/bundle_yellow_open`, `items/bundle_yellow_open_back_mers`
- `items/bundle_yellow_open_front_mers`, `items/bundle_yellow_open_mers`, `items/burn_pottery_sherd_mers`, `items/bush`, `items/bush_mers`, `items/cake_mers`
- `items/campfire_mers`, `items/candles/black_candle_mers`, `items/candles/blue_candle_mers`, `items/candles/brown_candle_mers`, `items/candles/candle_mers`, `items/candles/cyan_candle_mers`
- `items/candles/gray_candle_mers`, `items/candles/green_candle_mers`, `items/candles/light_blue_candle_mers`, `items/candles/light_gray_candle_mers`, `items/candles/lime_candle_mers`, `items/candles/magenta_candle_mers`
- `items/candles/orange_candle_mers`, `items/candles/pink_candle_mers`, `items/candles/purple_candle_mers`, `items/candles/red_candle_mers`, `items/candles/white_candle_mers`, `items/candles/yellow_candle_mers`
- `items/carrot_golden_mers`, `items/carrot_mers`, `items/carrot_on_a_stick_mers`, `items/cauldron_mers`, `items/chain_mers`, `items/chainmail_boots_mers`
- `items/chainmail_chestplate_mers`, `items/chainmail_helmet_mers`, `items/chainmail_leggings_mers`, `items/chalkboard_large`, `items/chalkboard_large_mers`, `items/chalkboard_medium`
- `items/chalkboard_medium_mers`, `items/chalkboard_small`, `items/chalkboard_small_mers`, `items/charcoal_mers`, `items/cherry_boat_mers`, `items/cherry_chest_boat_mers`
- `items/cherry_door_mers`, `items/cherry_hanging_sign_mers`, `items/cherry_sign_mers`, `items/chicken_cooked_mers`, `items/chicken_raw_mers`, `items/chorus_fruit_mers`
- `items/chorus_fruit_popped_mers`, `items/clay_ball_mers`, `items/clock_item_mers`, `items/coal_mers`, `items/coast_armor_trim_smithing_template_mers`, `items/comparator_mers`
- `items/compass_atlas_mers`, `items/compass_item_mers`, `items/cookie_mers`, `items/copper_axe_mers`, `items/copper_boots_mers`, `items/copper_chain_mers`
- `items/copper_chestplate_mers`, `items/copper_door_mers`, `items/copper_helmet_mers`, `items/copper_hoe_mers`, `items/copper_horse_armor_mers`, `items/copper_ingot_mers`
- `items/copper_lantern_mers`, `items/copper_leggings_mers`, `items/copper_nugget_mers`, `items/copper_pickaxe_mers`, `items/copper_shovel_mers`, `items/copper_sword_mers`
- `items/creeper_banner_pattern_mers`, `items/crimson_door_mers`, `items/crimson_hanging_sign_mers`, `items/crossbow_arrow_mers`, `items/crossbow_firework_mers`, `items/crossbow_pulling_0_mers`
- `items/crossbow_pulling_1_mers`, `items/crossbow_pulling_2_mers`, `items/crossbow_standby_mers`, `items/cyan_cushion`, `items/cyan_cushion_mers`, `items/danger_pottery_sherd_mers`
- `items/dark_oak_chest_boat_mers`, `items/dark_oak_hanging_sign_mers`, `items/diamond_axe_mers`, `items/diamond_boots_mers`, `items/diamond_chestplate_mers`, `items/diamond_helmet_mers`
- `items/diamond_hoe_mers`, `items/diamond_horse_armor_mers`, `items/diamond_leggings_mers`, `items/diamond_mers`, `items/diamond_pickaxe_mers`, `items/diamond_shovel_mers`
- `items/diamond_sword_mers`, `items/disc_fragment_5_mers`, `items/door_acacia_mers`, `items/door_birch_mers`, `items/door_dark_oak_mers`, `items/door_iron_mers`
- `items/door_jungle_mers`, `items/door_spruce_mers`, `items/door_wood_mers`, `items/dragon_fireball`, `items/dragon_fireball_mers`, `items/dragons_breath_mers`
- `items/dried_kelp_mers`, `items/dune_armor_trim_smithing_template_mers`, `items/dye_powder_black_mers`, `items/dye_powder_black_new_mers`, `items/dye_powder_blue_mers`, `items/dye_powder_blue_new_mers`
- `items/dye_powder_brown_mers`, `items/dye_powder_brown_new_mers`, `items/dye_powder_cyan_mers`, `items/dye_powder_glow_mers`, `items/dye_powder_gray_mers`, `items/dye_powder_green_mers`
- `items/dye_powder_light_blue_mers`, `items/dye_powder_lime_mers`, `items/dye_powder_magenta_mers`, `items/dye_powder_orange_mers`, `items/dye_powder_pink_mers`, `items/dye_powder_purple_mers`
- `items/dye_powder_red_mers`, `items/dye_powder_silver_mers`, `items/dye_powder_white_mers`, `items/dye_powder_white_new_mers`, `items/dye_powder_yellow_mers`, `items/echo_shard_mers`
- `items/egg_agent`, `items/egg_bat`, `items/egg_bee`, `items/egg_blaze`, `items/egg_cat`, `items/egg_cave_spider`
- `items/egg_chicken`, `items/egg_clownfish`, `items/egg_cod`, `items/egg_cow`, `items/egg_creeper`, `items/egg_dolphin`
- `items/egg_donkey`, `items/egg_drowned`, `items/egg_elderguardian`, `items/egg_enderman`, `items/egg_endermite`, `items/egg_evoker`
- `items/egg_fish`, `items/egg_fox`, `items/egg_ghast`, `items/egg_glow_squid`, `items/egg_guardian`, `items/egg_horse`
- `items/egg_husk`, `items/egg_lava_slime`, `items/egg_llama`, `items/egg_mask`, `items/egg_mers`, `items/egg_mule`
- `items/egg_mushroomcow`, `items/egg_npc`, `items/egg_npc_mers`, `items/egg_null`, `items/egg_ocelot`, `items/egg_panda`
- `items/egg_parrot`, `items/egg_phantom`, `items/egg_pig`, `items/egg_pigzombie`, `items/egg_pillager`, `items/egg_polarbear`
- `items/egg_pufferfish`, `items/egg_rabbit`, `items/egg_ravager`, `items/egg_salmon`, `items/egg_sheep`, `items/egg_shulker`
- `items/egg_silverfish`, `items/egg_skeleton`, `items/egg_skeletonhorse`, `items/egg_slime`, `items/egg_spider`, `items/egg_squid`
- `items/egg_stray`, `items/egg_turtle`, `items/egg_vex`, `items/egg_villager`, `items/egg_vindicator`, `items/egg_wanderingtrader`
- `items/egg_witch`, `items/egg_wither`, `items/egg_wolf`, `items/egg_zombie`, `items/egg_zombiehorse`, `items/egg_zombievillager`
- `items/elytra_mers`, `items/emerald_mers`, `items/empty_armor_slot_boots_mers`, `items/empty_armor_slot_chestplate_mers`, `items/empty_armor_slot_helmet_mers`, `items/empty_armor_slot_leggings_mers`
- `items/empty_armor_slot_shield_mers`, `items/end_crystal_mers`, `items/ender_eye_mers`, `items/ender_pearl_mers`, `items/experience_bottle_mers`, `items/explorer_pottery_sherd_mers`
- `items/exposed_copper_chain_mers`, `items/exposed_copper_door_mers`, `items/exposed_copper_lantern_mers`, `items/eye_armor_trim_smithing_template_mers`, `items/feather_mers`, `items/field_masoned_banner_pattern_mers`
- `items/fireball_mers`, `items/firefly_bush`, `items/firefly_bush_mers`, `items/fireworks_charge`, `items/fireworks_charge_mers`, `items/fireworks_mers`
- `items/fish_clownfish_raw_mers`, `items/fish_cooked_mers`, `items/fish_pufferfish_raw_mers`, `items/fish_raw_mers`, `items/fish_salmon_cooked_mers`, `items/fish_salmon_raw_mers`
- `items/fishing_rod_cast_mers`, `items/fishing_rod_uncast_mers`, `items/flint_and_steel_mers`, `items/flint_mers`, `items/flow_armor_trim_smithing_template_mers`, `items/flow_banner_pattern_mers`
- `items/flow_pottery_sherd_mers`, `items/flower_banner_pattern_mers`, `items/flower_pot_mers`, `items/friend_pottery_sherd_mers`, `items/ghast_tear_mers`, `items/globe_banner_pattern_mers`
- `items/glow_berries_mers`, `items/glow_item_frame_mers`, `items/glowstone_dust_mers`, `items/goat_horn_mers`, `items/gold_axe_mers`, `items/gold_boots_mers`
- `items/gold_chestplate_mers`, `items/gold_helmet_mers`, `items/gold_hoe_mers`, `items/gold_horse_armor_mers`, `items/gold_ingot_mers`, `items/gold_leggings_mers`
- `items/gold_nugget_mers`, `items/gold_pickaxe_mers`, `items/gold_shovel_mers`, `items/gold_sword_mers`, `items/gray_cushion`, `items/gray_cushion_mers`
- `items/green_cushion`, `items/green_cushion_mers`, `items/gunpowder_mers`, `items/guster_banner_pattern_mers`, `items/guster_pottery_sherd_mers`, `items/hanging_roots`
- `items/hanging_roots_mers`, `items/harness/harness_black_mers`, `items/harness/harness_blue_mers`, `items/harness/harness_brown_mers`, `items/harness/harness_cyan_mers`, `items/harness/harness_gray_mers`
- `items/harness/harness_green_mers`, `items/harness/harness_light_blue_mers`, `items/harness/harness_light_gray_mers`, `items/harness/harness_lime_mers`, `items/harness/harness_magenta_mers`, `items/harness/harness_orange_mers`
- `items/harness/harness_pink_mers`, `items/harness/harness_purple_mers`, `items/harness/harness_red_mers`, `items/harness/harness_white_mers`, `items/harness/harness_yellow_mers`, `items/heart_pottery_sherd_mers`
- `items/heartbreak_pottery_sherd_mers`, `items/heartofthesea_closed_mers`, `items/hoglin_meat_cooked_mers`, `items/hoglin_meat_raw_mers`, `items/honey_bottle_mers`, `items/honeycomb`
- `items/honeycomb_mers`, `items/hopper_mers`, `items/host_armor_trim_smithing_template_mers`, `items/howl_pottery_sherd_mers`, `items/iron_axe_mers`, `items/iron_boots_mers`
- `items/iron_chestplate_mers`, `items/iron_helmet_mers`, `items/iron_hoe_mers`, `items/iron_horse_armor`, `items/iron_horse_armor_mers`, `items/iron_ingot_mers`
- `items/iron_leggings_mers`, `items/iron_nugget_mers`, `items/iron_pickaxe_mers`, `items/iron_shovel_mers`, `items/iron_sword_mers`, `items/item_frame_mers`
- `items/jungle_chest_boat_mers`, `items/jungle_hanging_sign_mers`, `items/kelp_mers`, `items/lantern_mers`, `items/lead_mers`, `items/leaf_litter_mers`
- `items/leather_boots`, `items/leather_boots_mers`, `items/leather_chestplate_mers`, `items/leather_helmet`, `items/leather_helmet_mers`, `items/leather_horse_armor`
- `items/leather_horse_armor_mers`, `items/leather_leggings`, `items/leather_leggings_mers`, `items/leather_mers`, `items/lever`, `items/lever_mers`
- `items/light_block_0`, `items/light_block_0_mers`, `items/light_block_1`, `items/light_block_10`, `items/light_block_10_mers`, `items/light_block_11`
- `items/light_block_11_mers`, `items/light_block_12`, `items/light_block_12_mers`, `items/light_block_13`, `items/light_block_13_mers`, `items/light_block_14`
- `items/light_block_14_mers`, `items/light_block_15`, `items/light_block_15_mers`, `items/light_block_1_mers`, `items/light_block_2`, `items/light_block_2_mers`
- `items/light_block_3`, `items/light_block_3_mers`, `items/light_block_4`, `items/light_block_4_mers`, `items/light_block_5`, `items/light_block_5_mers`
- `items/light_block_6`, `items/light_block_6_mers`, `items/light_block_7`, `items/light_block_7_mers`, `items/light_block_8`, `items/light_block_8_mers`
- `items/light_block_9`, `items/light_block_9_mers`, `items/light_blue_cushion`, `items/light_blue_cushion_mers`, `items/light_gray_cushion`, `items/light_gray_cushion_mers`
- `items/lime_cushion`, `items/lime_cushion_mers`, `items/lodestonecompass_atlas_mers`, `items/lodestonecompass_item_mers`, `items/mace_mers`, `items/magenta_cushion`
- `items/magenta_cushion_mers`, `items/magma_cream_mers`, `items/mangrove_boat_mers`, `items/mangrove_chest_boat_mers`, `items/mangrove_door_mers`, `items/mangrove_hanging_sign_mers`
- `items/mangrove_propagule_mers`, `items/mangrove_sign_mers`, `items/map_abandoned_campsite`, `items/map_abandoned_campsite_mers`, `items/map_ancient_city`, `items/map_ancient_city_mers`
- `items/map_desert_pyramid`, `items/map_desert_pyramid_mers`, `items/map_desert_village`, `items/map_desert_village_mers`, `items/map_empty`, `items/map_empty_mers`
- `items/map_filled`, `items/map_filled_mers`, `items/map_jungle_temple`, `items/map_jungle_temple_mers`, `items/map_locked`, `items/map_locked_mers`
- `items/map_mansion`, `items/map_mansion_mers`, `items/map_mineshaft`, `items/map_mineshaft_mers`, `items/map_monument`, `items/map_monument_mers`
- `items/map_nautilus`, `items/map_nautilus_mers`, `items/map_plains_village`, `items/map_plains_village_mers`, `items/map_savanna_village`, `items/map_savanna_village_mers`
- `items/map_snowy_village`, `items/map_snowy_village_mers`, `items/map_swamp_hut`, `items/map_swamp_hut_mers`, `items/map_taiga_village`, `items/map_taiga_village_mers`
- `items/map_trial_chambers`, `items/map_trial_chambers_mers`, `items/map_warm_ocean_ruins`, `items/map_warm_ocean_ruins_mers`, `items/melon_mers`, `items/melon_speckled_mers`
- `items/minecart_chest_mers`, `items/minecart_command_block_mers`, `items/minecart_furnace_mers`, `items/minecart_hopper_mers`, `items/minecart_normal_mers`, `items/minecart_tnt_mers`
- `items/miner_pottery_sherd_mers`, `items/mojang_banner_pattern_mers`, `items/mourner_pottery_sherd_mers`, `items/mushroom_stew_mers`, `items/music_disc_bounce`, `items/music_disc_bounce_mers`
- `items/music_disc_creator_mers`, `items/music_disc_creator_music_box_mers`, `items/music_disc_lava_chicken_mers`, `items/music_disc_precipice_mers`, `items/music_disc_relic_mers`, `items/music_disc_tears_mers`
- `items/mutton_cooked_mers`, `items/mutton_raw_mers`, `items/name_tag_mers`, `items/nautilus_armor/copper_nautilus_armor_mers`, `items/nautilus_armor/diamond_nautilus_armor_mers`, `items/nautilus_armor/gold_nautilus_armor_mers`
- `items/nautilus_armor/iron_nautilus_armor_mers`, `items/nautilus_armor/netherite_nautilus_armor_mers`, `items/nautilus_mers`, `items/nether_sprouts_mers`, `items/nether_star_mers`, `items/nether_wart_mers`
- `items/netherbrick_mers`, `items/netherite_axe_mers`, `items/netherite_boots_mers`, `items/netherite_chestplate_mers`, `items/netherite_helmet_mers`, `items/netherite_hoe_mers`
- `items/netherite_horse_armor_mers`, `items/netherite_ingot_mers`, `items/netherite_leggings_mers`, `items/netherite_pickaxe_mers`, `items/netherite_scrap_mers`, `items/netherite_shovel_mers`
- `items/netherite_sword_mers`, `items/netherite_upgrade_smithing_template_mers`, `items/oak_chest_boat_mers`, `items/oak_hanging_sign_mers`, `items/ominous_bottle_mers`, `items/ominous_trial_key_mers`
- `items/orange_cushion`, `items/orange_cushion_mers`, `items/oxidized_copper_chain_mers`, `items/oxidized_copper_door_mers`, `items/oxidized_copper_lantern_mers`, `items/painting_mers`
- `items/pale_oak_boat`, `items/pale_oak_boat_mers`, `items/pale_oak_chest_boat`, `items/pale_oak_chest_boat_mers`, `items/pale_oak_door_mers`, `items/pale_oak_hanging_sign_mers`
- `items/pale_oak_sign_mers`, `items/paper_mers`, `items/phantom_membrane_mers`, `items/piglin_banner_pattern_mers`, `items/pink_cushion`, `items/pink_cushion_mers`
- `items/pink_petals_mers`, `items/pitcher_pod_mers`, `items/plenty_pottery_sherd_mers`, `items/poplar_boat`, `items/poplar_boat_mers`, `items/poplar_chest_boat`
- `items/poplar_chest_boat_mers`, `items/poplar_door`, `items/poplar_door_mers`, `items/poplar_hanging_sign`, `items/poplar_hanging_sign_mers`, `items/poplar_sign`
- `items/poplar_sign_mers`, `items/porkchop_cooked_mers`, `items/porkchop_raw_mers`, `items/potato_baked_mers`, `items/potato_mers`, `items/potato_poisonous_mers`
- `items/potion_bottle_absorption`, `items/potion_bottle_absorption_mers`, `items/potion_bottle_blindness`, `items/potion_bottle_blindness_mers`, `items/potion_bottle_confusion`, `items/potion_bottle_confusion_mers`
- `items/potion_bottle_damageBoost`, `items/potion_bottle_damageBoost_mers`, `items/potion_bottle_digSlowdown`, `items/potion_bottle_digSlowdown_mers`, `items/potion_bottle_digSpeed`, `items/potion_bottle_digSpeed_mers`
- `items/potion_bottle_drinkable`, `items/potion_bottle_drinkable_mers`, `items/potion_bottle_empty_mers`, `items/potion_bottle_fireResistance`, `items/potion_bottle_fireResistance_mers`, `items/potion_bottle_harm`
- `items/potion_bottle_harm_mers`, `items/potion_bottle_heal`, `items/potion_bottle_heal_mers`, `items/potion_bottle_healthBoost`, `items/potion_bottle_healthBoost_mers`, `items/potion_bottle_hunger`
- `items/potion_bottle_hunger_mers`, `items/potion_bottle_infested`, `items/potion_bottle_infested_mers`, `items/potion_bottle_invisibility`, `items/potion_bottle_invisibility_mers`, `items/potion_bottle_jump`
- `items/potion_bottle_jump_mers`, `items/potion_bottle_levitation`, `items/potion_bottle_levitation_mers`, `items/potion_bottle_lingering`, `items/potion_bottle_lingering_damageBoost`, `items/potion_bottle_lingering_damageBoost_mers`
- `items/potion_bottle_lingering_empty_mers`, `items/potion_bottle_lingering_fireResistance`, `items/potion_bottle_lingering_fireResistance_mers`, `items/potion_bottle_lingering_harm`, `items/potion_bottle_lingering_harm_mers`, `items/potion_bottle_lingering_heal`
- `items/potion_bottle_lingering_heal_mers`, `items/potion_bottle_lingering_infested`, `items/potion_bottle_lingering_infested_mers`, `items/potion_bottle_lingering_invisibility`, `items/potion_bottle_lingering_invisibility_mers`, `items/potion_bottle_lingering_jump`
- `items/potion_bottle_lingering_jump_mers`, `items/potion_bottle_lingering_luck`, `items/potion_bottle_lingering_luck_mers`, `items/potion_bottle_lingering_mers`, `items/potion_bottle_lingering_moveSlowdown`, `items/potion_bottle_lingering_moveSlowdown_mers`
- `items/potion_bottle_lingering_moveSpeed`, `items/potion_bottle_lingering_moveSpeed_mers`, `items/potion_bottle_lingering_nightVision`, `items/potion_bottle_lingering_nightVision_mers`, `items/potion_bottle_lingering_oozing`, `items/potion_bottle_lingering_oozing_mers`
- `items/potion_bottle_lingering_poison`, `items/potion_bottle_lingering_poison_mers`, `items/potion_bottle_lingering_regeneration`, `items/potion_bottle_lingering_regeneration_mers`, `items/potion_bottle_lingering_slowFall`, `items/potion_bottle_lingering_slowFall_mers`
- `items/potion_bottle_lingering_turtleMaster`, `items/potion_bottle_lingering_turtleMaster_mers`, `items/potion_bottle_lingering_waterBreathing`, `items/potion_bottle_lingering_waterBreathing_mers`, `items/potion_bottle_lingering_weakness`, `items/potion_bottle_lingering_weakness_mers`
- `items/potion_bottle_lingering_weaving`, `items/potion_bottle_lingering_weaving_mers`, `items/potion_bottle_lingering_windCharged`, `items/potion_bottle_lingering_windCharged_mers`, `items/potion_bottle_lingering_wither`, `items/potion_bottle_lingering_wither_mers`
- `items/potion_bottle_moveSlowdown`, `items/potion_bottle_moveSlowdown_mers`, `items/potion_bottle_moveSpeed`, `items/potion_bottle_moveSpeed_mers`, `items/potion_bottle_nightVision`, `items/potion_bottle_nightVision_mers`
- `items/potion_bottle_oozing`, `items/potion_bottle_oozing_mers`, `items/potion_bottle_poison`, `items/potion_bottle_poison_mers`, `items/potion_bottle_regeneration`, `items/potion_bottle_regeneration_mers`
- `items/potion_bottle_resistance`, `items/potion_bottle_resistance_mers`, `items/potion_bottle_saturation`, `items/potion_bottle_saturation_mers`, `items/potion_bottle_slowFall`, `items/potion_bottle_slowFall_mers`
- `items/potion_bottle_splash`, `items/potion_bottle_splash_absorption`, `items/potion_bottle_splash_absorption_mers`, `items/potion_bottle_splash_blindness`, `items/potion_bottle_splash_blindness_mers`, `items/potion_bottle_splash_confusion`
- `items/potion_bottle_splash_confusion_mers`, `items/potion_bottle_splash_damageBoost`, `items/potion_bottle_splash_damageBoost_mers`, `items/potion_bottle_splash_digSlowdown`, `items/potion_bottle_splash_digSlowdown_mers`, `items/potion_bottle_splash_digSpeed`
- `items/potion_bottle_splash_digSpeed_mers`, `items/potion_bottle_splash_fireResistance`, `items/potion_bottle_splash_fireResistance_mers`, `items/potion_bottle_splash_harm`, `items/potion_bottle_splash_harm_mers`, `items/potion_bottle_splash_heal`
- `items/potion_bottle_splash_heal_mers`, `items/potion_bottle_splash_healthBoost`, `items/potion_bottle_splash_healthBoost_mers`, `items/potion_bottle_splash_hunger`, `items/potion_bottle_splash_hunger_mers`, `items/potion_bottle_splash_infested`
- `items/potion_bottle_splash_infested_mers`, `items/potion_bottle_splash_invisibility`, `items/potion_bottle_splash_invisibility_mers`, `items/potion_bottle_splash_jump`, `items/potion_bottle_splash_jump_mers`, `items/potion_bottle_splash_levitation`
- `items/potion_bottle_splash_levitation_mers`, `items/potion_bottle_splash_mers`, `items/potion_bottle_splash_moveSlowdown`, `items/potion_bottle_splash_moveSlowdown_mers`, `items/potion_bottle_splash_moveSpeed`, `items/potion_bottle_splash_moveSpeed_mers`
- `items/potion_bottle_splash_nightVision`, `items/potion_bottle_splash_nightVision_mers`, `items/potion_bottle_splash_oozing`, `items/potion_bottle_splash_oozing_mers`, `items/potion_bottle_splash_poison`, `items/potion_bottle_splash_poison_mers`
- `items/potion_bottle_splash_regeneration`, `items/potion_bottle_splash_regeneration_mers`, `items/potion_bottle_splash_resistance`, `items/potion_bottle_splash_resistance_mers`, `items/potion_bottle_splash_saturation`, `items/potion_bottle_splash_saturation_mers`
- `items/potion_bottle_splash_slowFall`, `items/potion_bottle_splash_slowFall_mers`, `items/potion_bottle_splash_turtleMaster`, `items/potion_bottle_splash_turtleMaster_mers`, `items/potion_bottle_splash_waterBreathing`, `items/potion_bottle_splash_waterBreathing_mers`
- `items/potion_bottle_splash_weakness`, `items/potion_bottle_splash_weakness_mers`, `items/potion_bottle_splash_weaving`, `items/potion_bottle_splash_weaving_mers`, `items/potion_bottle_splash_windCharged`, `items/potion_bottle_splash_windCharged_mers`
- `items/potion_bottle_splash_wither`, `items/potion_bottle_splash_wither_mers`, `items/potion_bottle_turtleMaster`, `items/potion_bottle_turtleMaster_mers`, `items/potion_bottle_waterBreathing`, `items/potion_bottle_waterBreathing_mers`
- `items/potion_bottle_weakness`, `items/potion_bottle_weakness_mers`, `items/potion_bottle_weaving`, `items/potion_bottle_weaving_mers`, `items/potion_bottle_windCharged`, `items/potion_bottle_windCharged_mers`
- `items/potion_bottle_wither`, `items/potion_bottle_wither_mers`, `items/potion_overlay`, `items/potion_overlay_mers`, `items/prismarine_crystals_mers`, `items/prismarine_shard_mers`
- `items/prize_pottery_sherd_mers`, `items/pumpkin_pie_mers`, `items/purple_cushion`, `items/purple_cushion_mers`, `items/quartz_mers`, `items/quiver_mers`
- `items/rabbit_cooked_mers`, `items/rabbit_foot_mers`, `items/rabbit_hide_mers`, `items/rabbit_raw_mers`, `items/rabbit_stew_mers`, `items/raiser_armor_trim_smithing_template_mers`
- `items/raw_copper_mers`, `items/raw_gold_mers`, `items/raw_iron_mers`, `items/record_11_mers`, `items/record_13_mers`, `items/record_5_mers`
- `items/record_blocks_mers`, `items/record_cat_mers`, `items/record_chirp_mers`, `items/record_far_mers`, `items/record_mall_mers`, `items/record_mellohi_mers`
- `items/record_otherside_mers`, `items/record_pigstep_mers`, `items/record_stal_mers`, `items/record_strad_mers`, `items/record_wait_mers`, `items/record_ward_mers`
- `items/recovery_compass_atlas_mers`, `items/recovery_compass_item_mers`, `items/red_cushion`, `items/red_cushion_mers`, `items/redstone_dust_mers`, `items/reeds_mers`
- `items/repeater_mers`, `items/resin_brick_mers`, `items/resin_clump_mers`, `items/rib_armor_trim_smithing_template_mers`, `items/rotten_flesh_mers`, `items/ruby_mers`
- `items/saddle_mers`, `items/scrape_pottery_sherd_mers`, `items/sea_pickle_mers`, `items/seeds_beetroot_mers`, `items/seeds_melon_mers`, `items/seeds_pumpkin_mers`
- `items/seeds_wheat_mers`, `items/sentry_armor_trim_smithing_template_mers`, `items/shaper_armor_trim_smithing_template_mers`, `items/sheaf_pottery_sherd_mers`, `items/shears_mers`, `items/shelter_pottery_sherd_mers`
- `items/shulker_shell_mers`, `items/sign_acacia_mers`, `items/sign_birch_mers`, `items/sign_crimson_mers`, `items/sign_darkoak_mers`, `items/sign_jungle_mers`
- `items/sign_mers`, `items/sign_spruce_mers`, `items/sign_warped_mers`, `items/silence_armor_trim_smithing_template_mers`, `items/skull_banner_pattern_mers`, `items/skull_pottery_sherd_mers`
- `items/slimeball_mers`, `items/sniffer_egg_mers`, `items/snort_pottery_sherd_mers`, `items/snout_armor_trim_smithing_template_mers`, `items/snowball_mers`, `items/soul_campfire_mers`
- `items/soul_lantern_mers`, `items/spawn_egg`, `items/spawn_egg_sulfur_cube`, `items/spawn_egg_sulfur_cube_mers`, `items/spawn_eggs/spawn_egg_agent`, `items/spawn_eggs/spawn_egg_agent_mers`
- `items/spawn_eggs/spawn_egg_allay_mers`, `items/spawn_eggs/spawn_egg_armadillo_mers`, `items/spawn_eggs/spawn_egg_axolotl_mers`, `items/spawn_eggs/spawn_egg_bat_mers`, `items/spawn_eggs/spawn_egg_bee_mers`, `items/spawn_eggs/spawn_egg_blaze_mers`
- `items/spawn_eggs/spawn_egg_bogged_mers`, `items/spawn_eggs/spawn_egg_breeze_mers`, `items/spawn_eggs/spawn_egg_camel_husk_mers`, `items/spawn_eggs/spawn_egg_camel_mers`, `items/spawn_eggs/spawn_egg_cat_mers`, `items/spawn_eggs/spawn_egg_cave_spider_mers`
- `items/spawn_eggs/spawn_egg_chicken_mers`, `items/spawn_eggs/spawn_egg_cod_mers`, `items/spawn_eggs/spawn_egg_copper_golem_mers`, `items/spawn_eggs/spawn_egg_cow_mers`, `items/spawn_eggs/spawn_egg_creaking_mers`, `items/spawn_eggs/spawn_egg_creeper_mers`
- `items/spawn_eggs/spawn_egg_dolphin_mers`, `items/spawn_eggs/spawn_egg_donkey_mers`, `items/spawn_eggs/spawn_egg_drowned_mers`, `items/spawn_eggs/spawn_egg_elder_guardian_mers`, `items/spawn_eggs/spawn_egg_ender_dragon_mers`, `items/spawn_eggs/spawn_egg_enderman_mers`
- `items/spawn_eggs/spawn_egg_endermite_mers`, `items/spawn_eggs/spawn_egg_evoker_mers`, `items/spawn_eggs/spawn_egg_fox_mers`, `items/spawn_eggs/spawn_egg_frog_mers`, `items/spawn_eggs/spawn_egg_ghast_mers`, `items/spawn_eggs/spawn_egg_glow_squid_mers`
- `items/spawn_eggs/spawn_egg_goat_mers`, `items/spawn_eggs/spawn_egg_guardian_mers`, `items/spawn_eggs/spawn_egg_happy_ghast_mers`, `items/spawn_eggs/spawn_egg_hoglin_mers`, `items/spawn_eggs/spawn_egg_horse_mers`, `items/spawn_eggs/spawn_egg_husk_mers`
- `items/spawn_eggs/spawn_egg_iron_golem_mers`, `items/spawn_eggs/spawn_egg_llama_mers`, `items/spawn_eggs/spawn_egg_magma_cube_mers`, `items/spawn_eggs/spawn_egg_mooshroom`, `items/spawn_eggs/spawn_egg_mooshroom_mers`, `items/spawn_eggs/spawn_egg_mule_mers`
- `items/spawn_eggs/spawn_egg_nautilus_mers`, `items/spawn_eggs/spawn_egg_ocelot_mers`, `items/spawn_eggs/spawn_egg_panda_mers`, `items/spawn_eggs/spawn_egg_parched_mers`, `items/spawn_eggs/spawn_egg_parrot_mers`, `items/spawn_eggs/spawn_egg_phantom_mers`
- `items/spawn_eggs/spawn_egg_pig_mers`, `items/spawn_eggs/spawn_egg_piglin_brute_mers`, `items/spawn_eggs/spawn_egg_piglin_mers`, `items/spawn_eggs/spawn_egg_pillager_mers`, `items/spawn_eggs/spawn_egg_polar_bear_mers`, `items/spawn_eggs/spawn_egg_pufferfish_mers`
- `items/spawn_eggs/spawn_egg_rabbit_mers`, `items/spawn_eggs/spawn_egg_ravager_mers`, `items/spawn_eggs/spawn_egg_salmon_mers`, `items/spawn_eggs/spawn_egg_sheep_mers`, `items/spawn_eggs/spawn_egg_shulker_mers`, `items/spawn_eggs/spawn_egg_silverfish_mers`
- `items/spawn_eggs/spawn_egg_skeleton_horse_mers`, `items/spawn_eggs/spawn_egg_skeleton_mers`, `items/spawn_eggs/spawn_egg_slime_mers`, `items/spawn_eggs/spawn_egg_sniffer_mers`, `items/spawn_eggs/spawn_egg_snow_golem_mers`, `items/spawn_eggs/spawn_egg_spider_mers`
- `items/spawn_eggs/spawn_egg_squid_mers`, `items/spawn_eggs/spawn_egg_stray_mers`, `items/spawn_eggs/spawn_egg_strider_mers`, `items/spawn_eggs/spawn_egg_sulfur_cube`, `items/spawn_eggs/spawn_egg_sulfur_cube_mers`, `items/spawn_eggs/spawn_egg_tadpole_mers`
- `items/spawn_eggs/spawn_egg_trader_llama_mers`, `items/spawn_eggs/spawn_egg_tropicalfish_mers`, `items/spawn_eggs/spawn_egg_turtle_mers`, `items/spawn_eggs/spawn_egg_vex_mers`, `items/spawn_eggs/spawn_egg_villager_mers`, `items/spawn_eggs/spawn_egg_vindicator_mers`
- `items/spawn_eggs/spawn_egg_wandering_trader_mers`, `items/spawn_eggs/spawn_egg_warden_mers`, `items/spawn_eggs/spawn_egg_witch_mers`, `items/spawn_eggs/spawn_egg_wither_mers`, `items/spawn_eggs/spawn_egg_wither_skeleton_mers`, `items/spawn_eggs/spawn_egg_wolf_mers`
- `items/spawn_eggs/spawn_egg_zoglin_mers`, `items/spawn_eggs/spawn_egg_zombie_horse_mers`, `items/spawn_eggs/spawn_egg_zombie_mers`, `items/spawn_eggs/spawn_egg_zombie_nautilus_mers`, `items/spawn_eggs/spawn_egg_zombie_villager_mers`, `items/spawn_eggs/spawn_egg_zombified_piglin_mers`
- `items/spear/copper_spear_mers`, `items/spear/diamond_spear_mers`, `items/spear/gold_spear_mers`, `items/spear/iron_spear_mers`, `items/spear/netherite_spear_mers`, `items/spear/stone_spear_mers`
- `items/spear/wood_spear_mers`, `items/spider_eye_fermented_mers`, `items/spider_eye_mers`, `items/spire_armor_trim_smithing_template_mers`, `items/spruce_chest_boat_mers`, `items/spruce_hanging_sign_mers`
- `items/spyglass_mers`, `items/stick_mers`, `items/stone_axe_mers`, `items/stone_hoe_mers`, `items/stone_pickaxe_mers`, `items/stone_shovel_mers`
- `items/stone_sword_mers`, `items/straw_bed`, `items/straw_bed_mers`, `items/string_mers`, `items/sugar_mers`, `items/suspicious_stew_mers`
- `items/sweet_berries_mers`, `items/tide_armor_trim_smithing_template_mers`, `items/tipped_arrow`, `items/tipped_arrow_base_mers`, `items/tipped_arrow_fireres`, `items/tipped_arrow_fireres_mers`
- `items/tipped_arrow_harm`, `items/tipped_arrow_harm_mers`, `items/tipped_arrow_head_mers`, `items/tipped_arrow_healing`, `items/tipped_arrow_healing_mers`, `items/tipped_arrow_infested`
- `items/tipped_arrow_infested_mers`, `items/tipped_arrow_invisibility`, `items/tipped_arrow_invisibility_mers`, `items/tipped_arrow_leaping`, `items/tipped_arrow_leaping_mers`, `items/tipped_arrow_luck`
- `items/tipped_arrow_luck_mers`, `items/tipped_arrow_mers`, `items/tipped_arrow_nightvision`, `items/tipped_arrow_nightvision_mers`, `items/tipped_arrow_oozing`, `items/tipped_arrow_oozing_mers`
- `items/tipped_arrow_poison`, `items/tipped_arrow_poison_mers`, `items/tipped_arrow_regen`, `items/tipped_arrow_regen_mers`, `items/tipped_arrow_slow`, `items/tipped_arrow_slow_mers`
- `items/tipped_arrow_slowfalling`, `items/tipped_arrow_slowfalling_mers`, `items/tipped_arrow_strength`, `items/tipped_arrow_strength_mers`, `items/tipped_arrow_swift`, `items/tipped_arrow_swift_mers`
- `items/tipped_arrow_turtlemaster`, `items/tipped_arrow_turtlemaster_mers`, `items/tipped_arrow_waterbreathing`, `items/tipped_arrow_waterbreathing_mers`, `items/tipped_arrow_weakness`, `items/tipped_arrow_weakness_mers`
- `items/tipped_arrow_weaving`, `items/tipped_arrow_weaving_mers`, `items/tipped_arrow_windCharged`, `items/tipped_arrow_windCharged_mers`, `items/tipped_arrow_wither`, `items/tipped_arrow_wither_mers`
- `items/torchflower_seeds_mers`, `items/totem_mers`, `items/trial_key_mers`, `items/trident_mers`, `items/turtle_egg_mers`, `items/turtle_helmet_mers`
- `items/turtle_shell_piece_mers`, `items/vex_armor_trim_smithing_template_mers`, `items/villagebell_mers`, `items/ward_armor_trim_smithing_template_mers`, `items/warped_door_mers`, `items/warped_fungus_on_a_stick_mers`
- `items/warped_hanging_sign_mers`, `items/watch_atlas`, `items/watch_atlas_mers`, `items/wayfinder_armor_trim_smithing_template_mers`, `items/weathered_copper_chain_mers`, `items/weathered_copper_door_mers`
- `items/weathered_copper_lantern_mers`, `items/wheat_mers`, `items/white_cushion`, `items/white_cushion_mers`, `items/wild_armor_trim_smithing_template_mers`, `items/wildflowers`
- `items/wildflowers_mers`, `items/wind_charge_mers`, `items/wolf_armor_dyed`, `items/wolf_armor_dyed_mers`, `items/wolf_armor_mers`, `items/wood_axe_mers`
- `items/wood_hoe_mers`, `items/wood_pickaxe_mers`, `items/wood_shovel_mers`, `items/wood_sword_mers`, `items/yellow_cushion`, `items/yellow_cushion_mers`

## Verification and limits

- PNG decoder: zlib + all five filters, colour types 0/2/3/4/6, bit depths 1-16, tRNS, Adam7. `selftest.py` checks it on synthetic images from an independent encoder (every type/depth/filter, tRNS, Adam7 including images too small for some passes) and on PNGs written by the Windows WIC encoder (`tools/png-oracle/make-pngs.ps1`).
- TGA decoder: types 2/10 (and 1/3/9/11), 24/32-bit, bottom-left and top-left origin, right-to-left. Every Bedrock TGA here is type 2 or 10, bottom-left origin; hundreds of them are pixel-identical to the Java PNG of the same texture, which confirms the orientation handling.
- Decode errors: 0.
- Pixel identity compares RGBA8 after zeroing the colour of fully transparent pixels; single-colour images (e.g. fully transparent ones, many blank PBR maps) never count as evidence.
- Model evidence for Java <-> Bedrock uses only representative Java states (default orientation from the app's `java_versions.tsv`, section 26.1) and skips Bedrock faces whose terrain key is an array of different files, because the array index is chosen in game code.
- Bedrock layering: a later `vanilla_x` layer replaces a file of the same path (either extension); `blocks.json` entries merge field by field (later layers often only change `sound`); terrain keys are replaced per key. The base `vanilla` pack has no version, so its files have an empty `from`.
- canonical check passed: 4574 canonicals, each owned by exactly one texture (block = bare 26.1.2 name, other Java textures keep their folder such as item/ or entity/, removed Java textures use java:<last path>, Bedrock-only use bedrock:<path>)
