# IcyModz420's Minecraft Studio — everything it does

One program for every Minecraft world: open it, look through it, fix it, change it, move it to another edition or console,
and make skins, textures, models and packs. Unofficial; not affiliated with Mojang or Microsoft.

**Your world is never changed behind your back.** Tools that change a world either save into it only when you press Save
(a backup is made first), or write a new copy and check every block of it. Converting, rescuing, placing, replacing,
reshaping and restoring always write a new copy.

## Where each feature works

Every feature below says what it **works on** (the app's Help → What Works Where shows the same as a table). The names mean:

| Name | Which games and devices |
|---|---|
| **Java** | Java Edition on PC (Windows, Mac, Linux), every version from Classic to 26.x |
| **Bedrock** | Today's Minecraft on any device: Windows PC, Android, iPhone and iPad, Xbox One and Series X\|S, PlayStation 4 and 5, Nintendo Switch (the same world files on all of them; the world has to be on this PC, as a world folder or a .mcworld) |
| **Pocket 0.x** | Pocket Edition 0.1 – 0.8 and the Raspberry Pi Edition |
| **consoles** | the seven Legacy Console Editions: Xbox 360, Xbox One Edition, PS3, PS4 Edition, PS Vita, Wii U, Switch Edition — the save copied off the console |
| **3DS** | New Nintendo 3DS Edition (a world dump from the 3DS's extra data) |
| **Classic/Indev** | Java Classic and Indev levels (one-file worlds of a fixed size) |
| **all** | every one of the above |
| **On this PC** | needs the game installed on this Windows PC |

Xbox One, PS4 and Switch had their own console editions before Bedrock came to them (2017 – 2019). A world made in today's
game on those consoles is a **Bedrock** world; one made in the old edition is a **consoles** save.

## Open and find your worlds

- **Open any world** — a world folder or a save file of any edition; the kind is found from what is inside.
  *Works on:* all.
- **Worlds on this PC** — every Java and Bedrock world on this PC listed by itself, with recent worlds on the start page.
  *Works on:* Java; Bedrock (on a Windows PC). Not Pocket 0.x, consoles, 3DS, Classic/Indev (copy the save onto this PC and open it).
- **Worlds from a phone** — copies worlds off an Android phone by USB cable or Wi-Fi, without rooting it.
  *Works on:* Bedrock, Pocket 0.x (Android phones and tablets). Not Java, consoles, 3DS, Classic/Indev.
- **NBT editor** — edit any NBT file directly (level.dat, players, structures, chunks), Java's and Bedrock's kinds, SNBT.
  *Works on:* all.
- **Edit level.dat** — the world's settings file in the NBT editor.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, 3DS; Classic/Indev (Indev only: Classic levels have none).
- **Bedrock database browser** — every record of a Bedrock world's database.
  *Works on:* Bedrock. Not Java, Pocket 0.x, consoles, 3DS, Classic/Indev.
- **Region file viewer** — one Java region file (blocks, entities and poi) chunk by chunk.
  *Works on:* Java. Not Bedrock, Pocket 0.x, consoles, 3DS, Classic/Indev.

## The map

- **Map of every dimension** — Overworld, Nether, End and mod dimensions; zoom, pan, a cave view from any height, overlays of time spent, last update and biomes.
  *Works on:* all.
- **Slime chunks** — the chunks slimes spawn in, also where nobody has been yet.
  *Works on:* Java (from the seed); Bedrock, Pocket 0.x. Not consoles, 3DS (their slime rule is not known); not Classic/Indev (no slimes).
- **Structures on the map** — villages, outposts, shipwrecks, buried treasure, temples, monuments, strongholds, mansions, fortresses, bastions, end cities, ancient cities, trail ruins, trial chambers, dungeons …, with a gold ring where loot has never been taken.
  *Works on:* Java; Bedrock, Pocket 0.x, 3DS (fewer: most are found from their loot chests and spawners); consoles (the structures a save keeps (newer saves keep them all; older ones are found from their loot chests and spawners)). Not Classic/Indev (they have none).
- **Nether portals and where they lead** — every portal you built, where each one leads, whether that one leads back, or that it makes a new portal.
  *Works on:* Java, Bedrock; consoles (their smaller Nether: 1:3 in the classic world, the save's own scale in bigger ones); 3DS (the portals only, not where they lead: its Nether's scale isn't known). Not Pocket 0.x, Classic/Indev (no Nether).
- **Where players last died** — a red cross on the map where each player last died.
  *Works on:* Java (1.19 and newer); Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (not saved).
- **Picture of a dimension** — the whole map as one PNG.
  *Works on:* all.
- **Web map** — the whole dimension as a zoomable map that opens in any browser, offline, with the spawn, players and structures marked.
  *Works on:* all.
- **Heights picture** — a dimension's ground as a 16-bit grey picture for terrain programs (water, plants and trees left out).
  *Works on:* all.
- **3D view** — turn a piece of the world, or any structure, in 3D.
  *Works on:* all.
- **Select chunks** — by hand, by area around the spawn or a player, grow or shrink; save and load selections as MCA Selector .csv.
  *Works on:* all.
- **Delete chunks** — so the game makes them again, picked by hand or by the pruner (far away, outside or inside a selection, unfinished, rarely visited).
  *Works on:* Java, consoles; Bedrock, Pocket 0.x, Classic/Indev (not "rarely visited": no time-spent record). Not 3DS (the 3DS chunk index is only partly known).
- **Open a chunk's NBT** — a chunk's raw data in the NBT editor (right-click the map).
  *Works on:* Java, Bedrock; consoles (its mobs, chests and ticks (in older saves the whole chunk), saved back into the save); 3DS (to look at). Not Pocket 0.x, Classic/Indev (no chunk NBT).

## Look through the world

- **Find blocks, items, mobs and text** — blocks, items in chests and in the shulker boxes inside them, mobs (riders too), words on signs and books, and every player's inventory and ender chest; marked on the map.
  *Works on:* all.
- **Lost pets and villagers** — every tamed dog, cat, parrot and horse with who owns it, and each villager's job and trades.
  *Works on:* Java, Bedrock; Pocket 0.x, consoles, 3DS (owners not named). Not Classic/Indev (no tamed mobs).
- **Bring the pets home** — puts lost pets beside their owner, in a copy.
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (their owners can't be matched to the players).
- **Busiest chunks (lag finder)** — where mobs, dropped items, hoppers and chests pile up, marked on the map.
  *Works on:* all.
- **Clear dropped items and experience orbs** — out of the busiest chunks, in a copy.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles. Not 3DS (a changed 3DS chunk makes the game delete the world); not Classic/Indev (no dropped items).
- **Lag machines** — only the devices built to make a world or server lag or crash: minecart and mob piles, item floods, solid blocks of pistons or observers, machines left running, summoning command blocks, book and chunk bans — never ordinary redstone or farms.
  *Works on:* all.
- **Clean up lag machines** — takes them out of a copy and checks none is left.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles. Not 3DS (a changed 3DS chunk makes the game delete the world); not Classic/Indev (not built there).
- **Mods and add-ons in a world** — which mods, add-ons and data packs a world holds blocks, mobs, items, biomes or a dimension of, how much of each, and which mods on this PC have them.
  *Works on:* Java (Forge, NeoForge, Fabric and Quilt mods and data packs; the mods in every launcher's folders on this PC); Bedrock (add-ons and the packs that bring them). Not Pocket 0.x, consoles, 3DS, Classic/Indev (these games take no mods or add-ons).
- **Compare with another world** — what changed since a backup, or what a conversion changed, block by block, marked on the map (even two different editions).
  *Works on:* all.
- **Bring chunks back from a backup** — griefing, a creeper hole or a bad update undone, into a copy; mobs that walked away are not doubled; the backup may even be another edition or console.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles; Classic/Indev (the blocks only (an Indev level's chests and mobs stay as they are)). Not 3DS (a changed 3DS chunk makes the game delete the world).
- **Check for problems** — what stops a world opening, crashes the game or throws players out, loses things or leaves a player stuck — damaged chunks, how they are stored, book and chunk bans and other lag machines, mods, packs, the game version and every player — each with its fix.
  *Works on:* Java, Bedrock (in the most depth: region tables (Java), the database and how every chunk is stored (Bedrock), mods not on this PC (Java), packs, the game version (Java)); Pocket 0.x, Classic/Indev (damaged chunks, lag machines, stuck players); consoles (damaged chunks, the save's archive, lag machines, stuck players); 3DS (damaged chunks, lag machines, the player (only shown: nothing is written into a 3DS world)).

## Build and change

- **Save a structure (crop a building)** — drag a box on the map; save it as Java .nbt, WorldEdit .schem, Litematica .litematic, Bedrock .mcstructure or MCEdit .schematic, for any Java version.
  *Works on:* all.
- **Place a structure** — any structure or schematic into a copy of a world, turned or mirrored, standing on the ground; every block checked.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (a changed 3DS chunk makes the game delete the world).
- **Check a build** — a schematic laid over your build: every missing or wrong block on the map, how much is done, and what is still to gather (Place window).
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (its window places into the world, which the 3DS can't take).
- **Structure file** — see any schematic, its material list, turn or mirror it, save it as another format or Java version, or as a 3D model (.obj) for Blender.
  *Works on:* all.
- **Replace or fill blocks** — rules like *_ore → stone or * → air in a box, in a copy, every block checked.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (a changed 3DS chunk makes the game delete the world).
- **Change the biome** — any biome in a box (grass and leaf colour, weather, which mobs spawn), in a copy.
  *Works on:* Java, Bedrock, consoles. Not Pocket 0.x (its biomes aren't written here); not 3DS (a changed 3DS chunk makes the game delete the world); not Classic/Indev (no biomes).
- **Shape the land** — flatten an area for a build, smooth it, raise it or lower it, with soft edges; trees, chests and mobs move with the ground and water keeps its level; in a copy, every column checked.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles; Classic/Indev (the ground's blocks only: an Indev level's chests and its player's spot are kept out of it). Not 3DS (a changed 3DS chunk makes the game delete the world).
- **A world's maps as pictures** — every map item of a world as a PNG.
  *Works on:* Java, Bedrock, consoles; 3DS (the maps its value store keeps). Not Pocket 0.x, Classic/Indev (they keep no maps).
- **Make map art** — any picture as map art (1 – 16 maps across and down) given to a player, or made from blocks you can place in survival and saved as schematics to build.
  *Works on:* Java (not 26.1's new folder layout yet); Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (map art is added to Java and Bedrock worlds).
- **New world from a heightmap** — land from any grey picture or real elevation data — grass, sand beaches, stone cliffs, snowy peaks and water — made as any edition and checked block by block.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (no 3DS writer).

## Players and items

- **Players and inventories** — every player, their inventory, armour, ender chest, health, food, XP and position; change and save.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles; 3DS (to look at: nothing is written into a 3DS world); Classic/Indev (Indev only: Classic has no players).
- **Copy items to another world** — a player's inventory, armour, off-hand and ender chest into a player of a Java, Bedrock, console or Pocket 0.x world, each item changed into that world's form and version (what it can't hold becomes paper named after it).
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, 3DS (into a Java, Bedrock, console or Pocket 0.x world); Classic/Indev (Indev only, into a Java, Bedrock, console or Pocket 0.x world).
- **Copy /give command** — right-click an item: the command that gives it in the world's own version's form, or the item as SNBT.
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (no commands to copy).
- **Move to safety** — a player stuck in a wall, lost in the void or trapped in the Nether put on safe ground (beside their bed, at the spawn or near x, z), with what they ride.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles; Classic/Indev (Indev only: Classic levels keep no player). Not 3DS (3DS players are not read yet).
- **Restore a Hardcore world from death** — the player back in Survival with full health, beside their bed.
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (no Hardcore).
- **Single-player and server players** — a player's things between level.dat and a server's player files (offline-mode names, online UUIDs).
  *Works on:* Java. Not Bedrock, Pocket 0.x, consoles, 3DS, Classic/Indev.
- **Statistics and advancements** — change any statistic; mark any advancement (from the game and the world's data packs) done or not done.
  *Works on:* Java. Not Bedrock, Pocket 0.x, consoles, 3DS, Classic/Indev.

## World settings (Cheats & game mode)

- **Game mode, difficulty, cheats, Hardcore and achievements** — including turning achievements back on.
  *Works on:* Java, Bedrock, Pocket 0.x, 3DS; consoles (host privileges). Not Classic/Indev (no settings to change).
- **Game rules** — every rule the world's file has, in its own type.
  *Works on:* Java, Bedrock, Pocket 0.x, 3DS; consoles (most console saves keep their host options outside the file read here). Not Classic/Indev (none).
- **World name** — the name in the game's world list (Bedrock's levelname.txt too); the folder keeps its name.
  *Works on:* Java, Bedrock, 3DS; Pocket 0.x (0.2 – 0.8 (0.1 keeps no name)). Not consoles (the console's list shows the save's own title); not Classic/Indev (not kept).
- **Spawn, time of day and weather** — set the world spawn (also by right-clicking the map), the time and the weather.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, 3DS; Classic/Indev (the spawn point only, by right-clicking the map (Indev, and Classic levels from 0.0.14a)).
- **World border** — the world border's centre and size.
  *Works on:* Java. Not Bedrock, Pocket 0.x, consoles, 3DS, Classic/Indev.

## Packs

- **World packs** — a world's data packs and resource pack (Java) or behaviour and resource packs (Bedrock): install, switch on and off, change their order, uninstall (to a "Removed packs" folder, never deleted).
  *Works on:* Java; Bedrock (packs installed in the game are found on a Windows PC). Not Pocket 0.x, consoles, 3DS, Classic/Indev.
- **Packs on this PC** — which worlds have which packs.
  *Works on:* Java, Bedrock (worlds on this PC). Not Pocket 0.x, consoles, 3DS, Classic/Indev.
- **Check a pack** — what the game would ignore or show wrong in a resource pack or a data pack.
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev.
- **New data pack or add-on** — set up right for any Java version (1.13 → 26.x) or Bedrock version, with an example recipe, loot table and advancement if you like.
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev.

## Convert and keep safe

- **Convert a world (from)** — any world to any edition and version, in a new folder, checked afterwards.
  *Works on:* all.
- **Convert a world (to)** — Java 26.1 → Infdev, Bedrock 26.x → 1.1, Pocket 0.9 – 0.16 and 0.2 – 0.8, all seven consoles (an Xbox 360 save goes into your own save package, rehashed), Classic, Indev.
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (no 3DS writer).
- **Change blocks as it converts** — say what a mod's blocks, or any blocks, become (thermal:ore → copper_ore).
  *Works on:* Java, Bedrock, Pocket 0.x, consoles, Classic/Indev. Not 3DS (no 3DS writer).
- **Backups** — back up now; every backup of a world, newest first; restore one as a new world beside it, or compare with it.
  *Works on:* all.
- **Automatic backups** — each world backed up by itself after you play it (Settings).
  *Works on:* Java, Bedrock (worlds played on this PC). Not Pocket 0.x, consoles, 3DS, Classic/Indev (played on another device).
- **Save a world as one file** — .mcworld (Bedrock: opening it imports it into the game) or .zip (Java).
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (its save is one file already).
- **Rescue a damaged world** — everything that can still be read saved into a new world, including chunks the region tables have lost and older copies of damaged chunks; or a repaired copy.
  *Works on:* Java, Bedrock; Pocket 0.x, consoles, 3DS, Classic/Indev (what still reads, converted into a new world).
- **Optimize storage** — rewrites region files or the database compactly.
  *Works on:* Java, Bedrock, consoles. Not Pocket 0.x, 3DS, Classic/Indev.

## Create

- **Skin Studio** — paint skins on the 3D model or the flat picture (classic and slim arms, HD), save for any edition and version, or install into the games on this PC.
  *Works on:* Java; Bedrock (custom skins load on Windows, phones and tablets). Not Pocket 0.x, consoles, 3DS, Classic/Indev (console skins come as DLC packs).
- **Texture Studio** — texture packs from the game's own textures, animated, saved for any Java version or for Bedrock, or installed; the texture shows on its block in 3D as you paint.
  *Works on:* Java, Bedrock. Not Pocket 0.x, consoles, 3DS, Classic/Indev (console packs come as DLC).
- **Block and item models** — any Java block or item model with your pack's textures: change its boxes, faces and textures and save it into the pack (Texture Studio → Models).
  *Works on:* Java (models read from Java installed on this PC). Not Bedrock, Pocket 0.x, consoles, 3DS, Classic/Indev.
- **Mob Studio** — every animal and monster painted on its 3D model.
  *Works on:* Java, Bedrock (3D models need Bedrock installed on this PC). Not Pocket 0.x, consoles, 3DS, Classic/Indev.
- **Animation Studio** — see every mob move; design animations and models.
  *Works on:* Bedrock (models from Bedrock on this PC). Not Java, Pocket 0.x, consoles, 3DS, Classic/Indev.

## Moving worlds between devices

- **Java Edition (Windows, Mac, Linux)** — Worlds are folders in the game's saves folder: `%APPDATA%\.minecraft\saves` on Windows, `~/Library/Application Support/minecraft/saves` on a Mac, `~/.minecraft/saves` on Linux. In the game: Singleplayer → choose the world → Edit → Open World Folder.
  This program lists them by itself (Worlds on this PC). A world converted into the saves folder shows up in Singleplayer.
- **Bedrock on a Windows PC** — This program finds the worlds by itself. In the game, the pencil beside a world → Export World saves a .mcworld; opening a .mcworld puts it into the game.
- **Bedrock on Android phones and tablets** — File → Get Worlds from an Android Phone copies them by USB cable or Wi-Fi, without rooting the phone.
  Or in the game: the pencil beside the world → Export World, and send the .mcworld to the PC. To put a world back, copy the .mcworld to the phone and open it.
- **Bedrock on iPhone and iPad** — In the game: the pencil beside the world → Export World, save it to Files and copy the .mcworld to the PC. To put a world back, open the .mcworld on the device.
- **Bedrock on Xbox, PlayStation and Switch** — A console can't copy world files itself; a Realm can carry them. On the console, upload the world to your Realm (Realm settings → Replace World); on a Windows PC, download it (Realm settings → Download World) and it is among the worlds on this PC.
  After working on it, upload it from the PC to the Realm the same way and download it on the console.
- **Xbox 360 Edition** — In the Xbox's settings (System → Storage), copy the Minecraft save onto a USB drive; on the PC, open the save file from the drive's Xbox360 folder.
  To put a world back: Convert to Xbox 360 writes it into a save package of yours (choose one from the drive), then copy it back onto the Xbox the same way.
- **PS3, PS Vita, Wii U, and the Xbox One, PS4 and Switch Editions** — These consoles keep their saves where only the console's own save tools reach them. This program reads the save files you copy off with them (a PS3 save has to be decrypted first: this program doesn't decrypt saves), and the converted save goes back the same way.
- **New Nintendo 3DS Edition** — This program reads a dump of the 3DS's extra data (the world folder a 3DS save manager makes). It doesn't write to the 3DS: a changed 3DS world is deleted by the game.

## Command line (mcstudio)

`mcstudio.exe` does the same from scripts: `info`, `targets`, `convert`, `find`, `structures`, `lag`, `mods`, `restore`, `replace`, `biome`,
`shape`, `heights`, `heightmap`, `move`, `maps`, `share`, `crop`, `structure`, `materials`, `obj`, `packcheck`, `newpack`. Each works on
the same editions as the window it matches, only reads what it is given and writes new files and folders. Run `mcstudio help`.

## Not there yet

- Structures from the seed in land nobody has visited (needs the game's whole terrain generator to be right).
- Writing to the 3DS (changed 3DS chunks make the game delete the world).
- Where portals lead on 3DS saves (its Nether's scale isn't known; Pocket 0.x has no Nether).
- Slime chunks on console and 3DS saves, and console pets' owners (no saved slimes or tamed pets to check against yet).
- Copying items into Pocket Edition 0.x worlds (how they number the inventory's slots isn't checked against a real save yet).
