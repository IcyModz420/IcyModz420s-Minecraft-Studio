# Third-party notices

IcyModz420's Minecraft Studio bundles no Minecraft game files. Block/skin/texture art, sounds and the game itself
are **read at runtime from the user's own installation** and are never copied into or distributed with
this program. "Minecraft" is a trademark of Mojang Synergies AB / Microsoft; this is an independent,
unofficial tool and is not affiliated with, endorsed by, or associated with Mojang or Microsoft.

The program embeds **mapping data only** — tables of block/item/entity/biome ids and names, numeric-id
histories, block-state upgrade schemas, map colours and data-version numbers. These are facts needed to
convert saves between versions and editions; they contain no Mojang artwork, audio or code. They come
from the following open-source projects, used under their licenses:

| Dataset (in `MinecraftStudio.Core/Data/`) | Source | License |
|---|---|---|
| `mappings/*` (Java↔Bedrock block/item/entity/biome maps, versions) | [PrismarineJS/minecraft-data](https://github.com/PrismarineJS/minecraft-data) | MIT |
| `mappings/bedrock_upgrade_schemas`, Bedrock canonical states & legacy ids | [pmmp/BedrockBlockUpgradeSchema](https://github.com/pmmp/BedrockBlockUpgradeSchema), [pmmp/BedrockData](https://github.com/pmmp/BedrockData) | CC0 1.0 (public domain) |
| `mappings/geyser_*` (Bedrock↔Java item/biome maps) | [GeyserMC/mappings](https://github.com/GeyserMC/mappings) | MIT |

Data under CC0 is dedicated to the public domain; it is used by permission of that dedication, with
thanks to the PocketMine-MP team. The MIT-licensed datasets are used under the MIT License, reproduced
below.

---

## MIT License — PrismarineJS/minecraft-data

Copyright (c) PrismarineJS contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

## MIT License — GeyserMC

Copyright (c) GeyserMC contributors

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the
following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO
EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR
THE USE OR OTHER DEALINGS IN THE SOFTWARE.
