# IcyModz420's Minecraft Studio

[![Latest release](https://img.shields.io/github/v/release/IcyModz420/IcyModz420s-Minecraft-Studio?label=download)](https://github.com/IcyModz420/IcyModz420s-Minecraft-Studio/releases/latest)
[![Downloads](https://img.shields.io/github/downloads/IcyModz420/IcyModz420s-Minecraft-Studio/total)](https://github.com/IcyModz420/IcyModz420s-Minecraft-Studio/releases)
![Windows 10 and 11](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)
![Editions](https://img.shields.io/badge/editions-Java%20%7C%20Bedrock%20%7C%20Console%20%7C%203DS-brightgreen)

One program for every Minecraft world you have. Open a world from Java, Bedrock, Pocket Edition, an old Xbox 360 or
PS3 save, a Wii U, a Switch or even a New 3DS, then look through it, fix it, edit it, or move it to another edition.

![3D view of a piece of a world](docs/images/3d-view.png)

## Download

1. Download the zip from the [latest release](https://github.com/IcyModz420/IcyModz420s-Minecraft-Studio/releases/latest).
2. Unzip it anywhere.
3. Run `MinecraftStudio.exe`.

Windows 10 or 11, 64-bit. No installer and nothing else to install. Your Java and Bedrock worlds show up in the list
on the left by themselves.

## What you can do with it

**Move a world to another edition.** Take your old Xbox 360 or PS3 world to Java or Bedrock, a Java world to Bedrock
or the other way round, or a new world back to an old version. Every block of the result is checked against the
original, and anything that couldn't come across is listed.

**Save a world that won't load.** A crash, a power cut or a bad update can leave a world that the game refuses to
open. Rescue reads everything that can still be read, including chunks the game's own index has lost, and saves it
into a new world.

**Find what you lost.** Diamonds in a chest somewhere, your dog that wandered off, the village you found once, a
sign you wrote years ago. Find searches blocks, items (inside shulker boxes too), mobs and text, and shows them on the
map. Pets are listed with their owner, and can be brought back to you.

**See your whole world.** A map of every dimension with villages, temples, shipwrecks, buried treasure, ancient
cities and trial chambers marked, a gold ring where the loot hasn't been taken, Nether portals and where each one
leads, slime chunks, biomes and where you last died. Export it as a picture or as a web map you can share.

**Build and change things.** Cut a building out of one world and place it in another, in any edition, turned or
mirrored. Works with WorldEdit `.schem`, Litematica `.litematic`, Java `.nbt`, Bedrock `.mcstructure` and MCEdit
`.schematic` files. Replace blocks, change biomes, flatten or smooth land for a build, or turn any picture into map
art.

**Fix your player.** Stuck in a wall, lost in the void or trapped in the Nether: put them back on safe ground. Bring
a Hardcore world back after a death, edit inventories, copy your items from one world or edition to another, or
edit statistics and advancements.

**Hunt down lag.** See which chunks are busiest, and find lag machines and book or chunk bans on a server world,
then clean them up.

**Check a modded world.** See which mods, add-ons and data packs a world needs, how much of each is in it, and which
of them you have installed (Forge, NeoForge, Fabric and Quilt, from any launcher).

**Make your own content.** Skin Studio, Texture Studio (with Java block models), Mob Studio and Animation Studio,
plus templates for data packs and Bedrock add-ons and a checker that finds mistakes in packs.

![Mods and add-ons in a world](docs/images/mods.png)

## Supported editions

| Edition | Versions |
| --- | --- |
| Java Edition | Classic and Indev through 26.x |
| Bedrock Edition | 1.0 through 26.x, on any device (Windows, Android, iOS, Xbox, PlayStation, Switch) |
| Pocket Edition | 0.1 through 0.16 |
| Legacy Console Edition | Xbox 360, Xbox One, PS3, PS4, PS Vita, Wii U, Switch |
| New Nintendo 3DS Edition | Opened and converted from (the 3DS deletes a world that was changed, so nothing is written to it) |

Bedrock worlds have to be on the PC as a world folder or a `.mcworld` file. Android worlds can be copied over USB or
Wi-Fi from inside the program. Console saves are opened from the files you copy off the console.

## Compatibility

Every feature and the editions it works on. ✅ works · ⚠️ works with a limit · 👁️ can be viewed but not changed ·
❌ not available. In the program, Help → What Works Where shows the same table with the reason for each limit, and
[FEATURES.md](FEATURES.md) explains them in full.

<!-- compatibility:start -->
**Open and find your worlds**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Open any world | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Worlds on this PC | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Worlds from a phone | ❌ | ⚠️ | ⚠️ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| NBT editor | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Edit level.dat | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| Bedrock database browser | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Region file viewer | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

**The map**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Map of every dimension | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Slime chunks | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Structures on the map | ✅ | ⚠️ | ⚠️ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ | ❌ |
| Nether portals and where they lead | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ | ❌ |
| Where players last died | ⚠️ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Picture of a dimension | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Web map | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Heights picture | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 3D view | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Select chunks | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Delete chunks | ✅ | ⚠️ | ⚠️ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ⚠️ |
| Open a chunk's NBT | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 👁️ | ❌ |

**Look through the world**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Find blocks, items, mobs and text | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Lost pets and villagers | ✅ | ✅ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ❌ |
| Bring the pets home | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Busiest chunks (lag finder) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Clear dropped items and experience orbs | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Lag machines | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Clean up lag machines | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Mods and add-ons in a world | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Compare with another world | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Bring chunks back from a backup | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ⚠️ |
| Check for damage | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

**Build and change**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Save a structure (crop a building) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Place a structure | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Check a build | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Structure file | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Replace or fill blocks | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Change the biome | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |
| Shape the land | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ⚠️ |
| A world's maps as pictures | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| Make map art | ⚠️ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| New world from a heightmap | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |

**Players and items**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Players and inventories | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | 👁️ | ⚠️ |
| Copy items to another world | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| Copy /give command | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Move to safety | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ⚠️ |
| Restore a Hardcore world from death | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Single-player and server players | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Statistics and advancements | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

**World settings (Cheats & game mode)**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Game mode, difficulty, cheats, Hardcore and achievements | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ |
| Game rules | ✅ | ✅ | ✅ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ✅ | ❌ |
| World name | ✅ | ✅ | ⚠️ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| Spawn, time of day and weather | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ⚠️ |
| World border | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

**Packs**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| World packs | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Packs on this PC | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Check a pack | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| New data pack or add-on | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

**Convert and keep safe**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Convert a world (from) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Convert a world (to) | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Change blocks as it converts | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ✅ |
| Backups | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Automatic backups | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Save a world as one file | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Rescue a damaged world | ✅ | ✅ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ | ⚠️ |
| Optimize storage | ✅ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ❌ | ❌ |

**Create**

| Feature | Java | Bedrock | Pocket 0.x | Xbox 360 | Xbox One Edition | PS3 | PS4 Edition | PS Vita | Wii U | Switch Edition | 3DS | Classic/Indev |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| Skin Studio | ✅ | ⚠️ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Texture Studio | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Block and item models | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Mob Studio | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Animation Studio | ❌ | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |

<!-- compatibility:end -->

## Is it safe for my worlds?

Yes. Anything that changes a world either makes a backup first and saves only when you press Save, or writes a new
copy of the world and checks every block of it. Converting, rescuing, placing, replacing, reshaping and restoring
never touch the original. Automatic backups can be turned on in Settings.

## FAQ

**Do I need Minecraft installed?** No, for opening and editing worlds. The skin, texture and mob studios use the
textures and models of the Minecraft you have installed, because none are included with this program.

**Does it work on Mac or Linux?** Not yet. It is a Windows program.

**Can I use it on a server world?** Yes. Open the world folder from the server, preferably while the server is
stopped. There are tools for moving players between single-player and server files.

**Does it support mods?** It opens modded Java worlds and keeps mod blocks, items and mobs when you edit them, and the
mods window tells you what a world needs. Converting a modded world to another edition turns mod blocks into the
closest vanilla blocks you choose.

## Command line

`mcstudio.exe` comes in the same zip and runs the same tools from scripts and batch files:

```
mcstudio info "C:\path\to\world"
mcstudio convert "C:\path\to\world" --to <target> --out "C:\path\to\new folder"
mcstudio targets
mcstudio help
```

## Building from source

You need Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```
dotnet build MinecraftStudio.sln -c Release
```

| Project | What it is |
| --- | --- |
| `MinecraftStudio.Core` | Save formats, conversion and every world tool, without UI |
| `MinecraftStudio` | The Windows program |
| `MinecraftStudio.Cli` | The `mcstudio` command line |
| `McTests` | Test harness, run against sample worlds that are not included in the repository |

## Support

Found a bug or a world that won't open? [Open an issue](https://github.com/IcyModz420/IcyModz420s-Minecraft-Studio/issues)
and say which edition and version the world is from.

If the tool helped you, the price is one subscription to **IcyModz420** on YouTube.

## Legal

Not an official Minecraft product. Not approved by or associated with Mojang or Microsoft. No Minecraft textures,
skins or models are included. Third-party data used by the converter is listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Copyright © 2026 IcyModz420. All rights reserved.
