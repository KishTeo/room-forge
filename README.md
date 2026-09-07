# RoomForge

A procedural 2D dungeon generator for Unity. You set up the rules in a visual
editor window — which room types can connect to what, how much branching to
allow, grid spacing — and it builds you a fresh layout at runtime, every run.

No external dependencies. Unity 2021.3 LTS or newer, 2D URP.

## Install

Drop the `Assets/RoomForge` folder into your project. That's it, it's
self-contained — but one heads-up: the `DoorAnchor_N` / `_S` / `_E` / `_W`
tags live in your project's Tag Manager, not inside the folder itself. If
you're copy-pasting the folder into an existing project instead of cloning
this whole repo, go add those 4 tags by hand first (Project Settings → Tags
and Layers), or Unity will complain.

## Quick start (15 minutes, give or take)

1. **Build your room prefabs.** Slap a `RoomDefinition` component on each
   one, pick a type (`StartRoom` / `NormalRoom` / `EndRoom` / `SpecialRoom`)
   and a size. Mark the spots where a corridor can plug in with child objects
   tagged `DoorAnchor_N` / `_S` / `_E` / `_W` — RoomForge finds them on its
   own, no wiring needed.

   Do this for **all 4 sides on every room**, even the ones you think will
   never actually connect to anything — that's where the door blocker goes,
   and RoomForge can only close a side if there's an anchor sitting on it
   (more on this in the gotchas section below). Also: one anchor per side.
   Tag two objects `DoorAnchor_N` by accident and only the last one wins,
   silently — they land in the same dictionary slot. If the anchor itself
   has a `SpriteRenderer` showing an open-door graphic, RoomForge hides it
   automatically when it blocks that side — but only if that sprite lives
   directly on the anchor object, not buried somewhere else in the hierarchy.

   (There's a working set already in the box —
   `Assets/RoomForge/Prefabs/Rooms/Space-room_*`, wired up on
   `Assets/RoomForge/SO/Space.asset`. Open that config to see a real example
   instead of guessing.)
2. **Make a config.** `DungeonConfigSO` (Create → RoomForge → Dungeon
   Config) — this is where you drop in your room prefabs by type, wire up
   which types can connect to which, set branching rules, and pick a grid
   size. Quick note on `Weight`: it only decides which prefab gets picked
   *among prefabs of the same type* (say, which of your three `NormalRoom`
   variants shows up) — it doesn't make that room type show up more often in
   the graph. Different knob, no knob for that.
3. **Open the editor window.** `Window → RoomForge` — visual connections
   graph, a live preview of the layout, and it'll flag config problems
   (missing prefabs, bad sizes) right there before you even hit Play.
4. **Put a generator in your scene.** Empty GameObject, add
   `DungeonGenerator`, assign your `DungeonConfigSO`.
5. **Call it from code:**

   ```csharp
   using RoomForge;
   using UnityEngine;

   public class LevelBootstrap : MonoBehaviour
   {
       [SerializeField] private DungeonGenerator generator;

       private void Start() => generator.Generate();
   }
   ```

Done — the level shows up in your scene under a `Dungeon` child object on the
generator.

## The Connections & Branching graph

The `Window → RoomForge` editor draws a graph called "Connections &
Branching." Worth understanding what it actually controls before you spend
time tweaking it:

- Connections only decide **what type the next room is** as you walk the
  critical path from Start to End. They don't touch room *count* (that's
  `Min/Max Room Count` in Generation Settings) and they can't shorten the
  path — wiring only `Start → End` will never give you a 2-room dungeon,
  that's just not what this graph does.
- **Some connections do literally nothing, even if you draw them.** The
  window will warn you about these, but so you're not left guessing:
  - anything going **into** `StartRoom` — it's always exactly one room and
    gets placed separately, never picked as "the next one";
  - anything going **into** `EndRoom` other than the automatic finish;
  - anything going **out of** `EndRoom` — the path always ends there, it
    never becomes the "current" room again;
  - anything going **into** `SpecialRoom` while `Keep Special Rooms Off
    Critical Path` is on (it's on by default) — in that mode `SpecialRoom`
    only ever shows up as a dead-end leaf, never part of the main path.

  Drawing these doesn't break anything, generation still works fine — it's
  just wasted clicks, so don't sweat getting them "right."
- `Branch Count` on a node is how many dead-end branches sprout off *each*
  room of that type on the path — not how deep one branch goes. Depth is the
  separate `Max Branch Depth` setting in Generation Settings.

## specialTag — optional, skip it if you don't need subtypes

*(Totally optional feature — if you just want treasure rooms and shop rooms
to be indistinguishable `SpecialRoom`s, ignore this whole section.)*

`specialTag` on a room entry is a free-text subtype — only makes sense for
`Type = SpecialRoom`. For any other type the field doesn't even show up in
the `Rooms` list, so if you're looking for it and can't find it, double
check the type on that entry.

`BranchRule → Dead-end` on the graph node is a dropdown, not a text field —
it's built from whatever tags already exist on at least one `SpecialRoom`
prefab in your config. Empty dropdown means no `SpecialRoom` prefab has that
tag yet — go tag one in the `Rooms` list first, then come back. The **"Any
Special Room"** option (blank) isn't "you forgot to tag something" — it's a
deliberate "don't filter, anything works" choice, handy for branches where
the reward type doesn't matter.

At runtime it's exposed as `RoomInstance.SpecialTag`. If a branch leaf asks
for a tag and nothing matches, `RoomPlacer` just falls back to any
`SpecialRoom` prefab and logs a warning — it won't blow up your generation.

## Spawning the player and content

```csharp
generator.OnGenerationComplete += () =>
{
    transform.position = generator.StartRoom.Position;
};

generator.OnRoomPlaced += room =>
{
    // room.Type, room.SpecialTag, room.Definition.SpawnPoints — do whatever you want here
};
```

`OnRoomPlaced` fires per room, right after it and its corridors are placed,
but before `OnGenerationComplete` — good spot to drop enemies/loot onto
`room.Definition.SpawnPoints` (those are just `SpawnPoint`-tagged markers you
place on the room prefab yourself).

*(Content spawning via `SpawnPoint`/`OnRoomPlaced` is entirely optional — the
plugin works fine without ever touching it if all you need is empty rooms.)*

A couple of things worth knowing if you go down this road:

- **One bad `OnRoomPlaced` subscriber won't take down the whole generation.**
  `DungeonGenerator` wraps each subscriber call in try/catch with
  `Debug.LogException` — so if your spawn code throws (unassigned prefab
  reference, whatever), generation keeps going and `OnGenerationComplete`
  still fires. Check the console, the exception's there, it just won't take
  the whole pipeline down with it.
- Whatever array you use for spawn candidates (`GameObject[]` or similar in
  your own subscriber code) happily accepts both project prefabs and scene
  objects dragged straight from the Hierarchy — Unity doesn't care where
  they came from.

## Advanced: locking a specific door

RoomForge deliberately doesn't know what a "key" or a "lock" is — that's
game logic, not layout logic. It just gives you the data to find the right
door yourself:

```csharp
generator.OnRoomPlaced += room =>
{
    foreach (RoomInstance neighbor in room.Neighbors)
    {
        RoomForge.DoorDirection? dir = room.GetDirectionTo(neighbor);
        if (dir.HasValue && room.Definition.DoorAnchors.TryGetValue(dir.Value, out Transform anchor))
        {
            // anchor.position is the world-space spot for that exact door.
            // Lock it, put the key in any SpecialRoom, your call from here.
        }
    }
};
```

## Gotchas / frequent problems

**Some rooms are left with open doorways that never got blocked.**
`RoomDefinition` only picks up direct children tagged
`DoorAnchor_N/_S/_E/_W`. Missing an anchor on even one side means RoomForge
can't block it — and it won't warn you, just silently skips it. Double-check
every room prefab in your config has exactly 4 tagged children, regardless
of whether a given layout actually uses all 4 sides.

**`OnGenerationComplete` / `OnRoomPlaced` seem to just... not fire** (player
doesn't move to the start room, nothing spawns, but no errors either).
Nine times out of ten it's an empty `DungeonGenerator` reference in the
Inspector on whatever's subscribing — subscribing to `null` is a silent
no-op. Check the field is actually filled in the *open scene*, not just in
the file on disk, and save — unsaved Inspector edits here drift from what's
on disk more easily than you'd think.

**The preview window throws up a "no valid layout found" warning.**
Not a bug, just an honest report: on that preview seed, the layout failed
the internal `Validator` check on every attempt (`maxGenerationAttempts`,
10 by default). Doesn't matter in an actual game — runtime generation
retries the exact same way, players never see it. If it keeps happening,
that's a sign your settings (high `Branch Count`, `Compact` solver with a
tight `Grid Cell Size`) are pushing close to what the overlap-resolution
pass can handle — try Reroll, dial back branching, or switch to `Grid`
(which can't overlap by construction, no best-effort involved).

**Resized a room in the prefab, but `Auto-size from Prefabs` in the main
window won't pick it up.** That button reads `RoomDefinition → Size` — a
plain saved number, not something computed live from the sprite. Select the
room prefab itself first and hit **Auto-size from Sprites** in its own
Inspector (measures the combined `SpriteRenderer` bounds of its children,
ignores colliders and any decoration hanging past the walls) — that updates
`Size`, and only then will `Auto-size from Prefabs` in the main window pick
up the real numbers.

**No corridors show up at all**, rooms just sit there disconnected, no
errors. Check `DungeonConfigSO.corridorPrefab` — if that reference is empty
(say you regenerated the corridor prefab and Unity gave it a new guid while
your config still points at the old one), `RoomPlacer.PlaceCorridors` quietly
builds nothing. The editor window does flag a missing corridor prefab, it's
just easy to scroll past.

**Player gets stuck in a doorway that's clearly open.** Almost never a
RoomForge bug — check your character's collider isn't wider than the actual
gap in the `DoorAnchor` opening. RoomForge only handles level geometry, not
your character's physics.

**1px seam between a room and its corridor.** Classic pixel-art-without-
pixel-snapping issue: at low PPU, a camera sitting at a non-integer
world-space position (like one following the player) rasterizes two
touching sprites onto different screen pixels, and you get background
peeking through the seam. Nothing to do with RoomForge — fix lives in your
scene's camera setup: grab the `com.unity.2d.pixel-perfect` package and add
a **Pixel Perfect Camera** component.

## Public API

```csharp
DungeonGenerator.Generate()            // generate a level
DungeonGenerator.Regenerate()          // clear + generate again

DungeonGenerator.OnGenerationComplete  // Action, fires after a successful generation
DungeonGenerator.OnRoomPlaced          // Action<RoomInstance>, fires per room

DungeonGenerator.Rooms                 // List<RoomInstance>
DungeonGenerator.StartRoom             // RoomInstance
DungeonGenerator.EndRoom               // RoomInstance

RoomInstance.Type                      // RoomType
RoomInstance.Neighbors                 // List<RoomInstance>
RoomInstance.Position                  // Vector2, world space
RoomInstance.SpecialTag                // string, SpecialRoom subtype (can be null)
RoomInstance.Definition.SpawnPoints    // List<Transform>
RoomInstance.GetDirectionTo(neighbor)  // DoorDirection?
```

## Two layout solvers

Pick one via `DungeonConfigSO.layoutSolverType`:

- **Grid** (default) — a uniform grid, step size is the biggest prefab in
  your config. Overlaps are impossible by construction. This is what you
  want for anything shipping.
- **Compact** — ⚠️ **still experimental, treat it as a test feature.** Same
  connection topology as Grid, but the spacing between any given pair of
  rooms comes from their actual `RoomDefinition.size` instead of one shared
  grid cell — so small rooms don't get stretched out to fit next to a huge
  boss room. The catch: after placing rooms it runs an overlap-resolution
  pass that's best-effort, not a hard guarantee, backed by the same retry
  loop as everything else. It's had a handful of real bugs shaken out
  already (floating-point tolerance, topology edge cases, subtree math),
  and it seems solid in testing so far, but it hasn't seen enough real
  mileage yet for me to call it production-ready. Use it, poke at it, just
  don't be surprised if it needs another round of fixes — and keep `Grid` as
  your fallback if it acts up.

## Known limitations

Deliberate scope calls, not forgotten bugs:

- **Every room prefab needs all 4 door openings pre-drawn** — RoomForge
  doesn't rotate or mirror rooms to fit a layout.
- **Only 4 cardinal neighbors** (`N/S/E/W`), no diagonal corridors.
- **No built-in progression primitives** (locked doors, mandatory forks) —
  just the data to build your own (see "Advanced" above).
- **Guarantees a connected, non-overlapping layout — nothing fancier.** No
  constraint-solving for "best" layouts on top of that, on purpose, so this
  thing doesn't grow into the complexity it's trying to avoid.

## Folder layout

```
Assets/RoomForge/
  Editor/       — the editor window (Window → RoomForge)
  Runtime/
    Core/       — DungeonGenerator, DungeonConfigSO, RoomDefinition, RoomInstance
    Generation/ — GraphBuilder, ILayoutSolver (Grid/Compact), RoomPlacer, Validator, BackgroundFiller
  Prefabs/      — the Space room/corridor set (working example)
  SO/           — Space.asset, an example DungeonConfigSO
  Sprites/
```

## Performance

30 rooms with no branching generate in a median ~5ms (about ×14 headroom
against the 100ms target on reasonable hardware).
