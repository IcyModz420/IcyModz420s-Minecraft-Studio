# Changelog

## 1.1.0

### Fixed: converted worlds that Bedrock wouldn't open

When Java saves a world, it also keeps a ring of half-made chunks around the land you've explored. Converting a Java
world to Bedrock wrote those as finished but empty chunks, and Bedrock couldn't open a world full of them.

- The converter now leaves those half-made chunks out, and Bedrock or Java makes them itself the first time you go there.
- Every Bedrock world the converter writes is now checked against how the game stores its chunks. A world that breaks
  those rules fails the converter's own checks instead of crashing the game.
- A world converted with 1.0.0 that won't open can be fixed: use **Rescue a damaged world** and **Save repaired copy**.
  The empty chunks are left out of the copy and the game makes them again. Your other chunks are kept exactly as they
  were.

### New: Check for problems

World menu, the world's Overview page, or the map's Tools menu. This replaces "Check for damage".

It reads the whole world and tells you what would stop it opening, crash the game or throw players out, lose things, or
leave a player stuck:

- chunks that can't be read, and damaged region files or databases
- chunks stored in a way the game can't open
- book bans, chunk bans and other lag machines
- mods a Java world needs that aren't on your PC, including mods that only left items behind
- missing packs, and data packs with mistakes
- a world saved by a newer Java version than any on your PC
- players who died in Hardcore, or are stuck inside a block, in lava or below the world

Each problem has a button that opens its fix: Rescue, Convert, Clean up, Move to safety, and so on. It works for every
edition and console save. From the command line: `mcstudio check <world>`.

### Rescue

- Bedrock chunks whose "Version" record was damaged get it back in the repaired copy, so their blocks load instead of
  the game generating new land over them.
- Leftover records that hold nothing are cleaned out of the copy. Anything that can't be recovered is listed.

### Animation Studio

- The side panel no longer cuts off its controls at high display scaling (150 % and 200 %).
- The timeline stays fast for long animations, and its time labels no longer overlap or run off the edge.
- Clicking a keyframe on the timeline highlights it.
- Keyboard shortcuts no longer take keys away from a focused button, list or check box: Space presses the button, and
  the arrows move through the list. After pressing "1 tick", the arrow keys still step through time.
- A keyframe drag that loses the mouse (switching windows mid-drag) now stops cleanly.

### Skin, Texture and Mob Studio

- Arrow keys and letters pressed in a list no longer nudge your selection on the picture or switch tools.

## 1.0.0

The first public release.
