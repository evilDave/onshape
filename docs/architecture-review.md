# Printable Thread — implement apply

Handoff after a completed `/grill-with-docs` session. The user confirmed the shared understanding. **Do not re-grill.** Implement.

Domain names live in [`CONTEXT.md`](../CONTEXT.md). Use them. Architecture words in this file: **module**, **interface**, **implementation**, **depth**, **deep**, **shallow**, **seam**, **adapter**, **leverage**, **locality**.

## Read first

1. [`CONTEXT.md`](../CONTEXT.md) — glossary only.
2. [`docs/adr/0001-apply-tool-to-face-and-locations.md`](adr/0001-apply-tool-to-face-and-locations.md)
3. [`docs/adr/0002-one-builder-for-keep-and-apply.md`](adr/0002-one-builder-for-keep-and-apply.md)
4. [`docs/adr/0003-apply-one-list-and-keep.md`](adr/0003-apply-one-list-and-keep.md)
5. [`docs/adr/0004-location-cuts-exactly-one-body.md`](adr/0004-location-cuts-exactly-one-body.md)
6. [`docs/adr/0005-same-face-external-threads-unsupported.md`](adr/0005-same-face-external-threads-unsupported.md)
7. [`docs/adr/0006-location-faces-bound-the-die.md`](adr/0006-location-faces-bound-the-die.md)
8. This file.

`printableThread.fs` is unmodified. Stay in that file. `createHelicalGroove` is closed; call it from the builder, do not reopen it.

## Done when

All of these are true:

- One **builder** produces a Tap or Die. Keep and apply both call it. No second derivation of the thread in the feature body.
- One **apply** module takes one tool plus one Location list (Matching or Mating) and iterates. Keep is an option on that apply.
- The Primary thread is that apply of the matching tool on the Thread face owner, not a separate `createHelicalGroove` subtract plus `applyInternalHoleFinish` / face `opChamfer`.
- Locations may cut the Thread face owner (`exclude` is empty).
- Stop at shortens the defined thread where planes cross the axis; envelope-only planes clip the Primary thread and kept tools, not a Location tool’s envelope.
- Dead part-pick helpers and hidden `boreParts` / `boreMatingPart` are gone.
- Operation IDs listed below still identify the same ops, so existing documents keep their faces.
- You have given the user the regen path at the bottom.

## Do this, in order

### 1. Builder module

**Done when:** `tapSpecFromDefinition` / `dieSpecFromDefinition` stamp `tool : "tap"` or `"die"`. One function (`buildThreadTool`) calls `createThreadedTap` or `createThreadedDie`. Callers set length with one helper, not by poking `tapLength` / `dieLength` in four places. Complementary radii and Thread clearance live on the spec; the feature body does not re-read `cutsInward` / `matchExternal` after construction.

`spec.height` stays the Thread face axial length (cone taper). Default *tool* length is the defined thread length (face extent, shortened by Stop at when planes cross the axis).

Set `generatorCSys` on both the matching spec and the complementary spec so Clock stays relative to the Primary thread.

### 2. Apply module

**Done when:** `cutTapLocations` and `cutDieLocations` are gone. One `applyThreadTool` owns place, End, Clock, Finish apply, Die envelope growth, boolean against Stock, skip, and cleanup.

**Interface (callers own):** which list; which already-built Tap or Die; End / Clock / Opposite defaults for that list; optional Primary block; optional keep block.

**Per row (instantiation, not a new tool):** Opposite, End, Clock, Finish overrides, Die envelope (`fitThreadOnly = false`, outer radius to owner extent + 0.5 mm). Matching versus Mating is not a per-row flag.

**Primary block (Matching apply only):** Thread face frame, defined length, Stop at, Stock = face owner. Fail hard (`Could not cut the thread from the part.`). Internal Primary: Tap, no envelope growth. External Primary: Die with `fitThreadOnly = true` (cut the pin, do not face the whole plate). Location Dies still grow.

**Keep block:** even if the list is empty. Boolean consumes cut copies; keep is another `buildThreadTool` instantiation of the same spec (names, mate connectors). Through tap/die stay packaging (`createThroughTapParts` / `createThroughDieParts`) at Through length. Clip kept solids with Stop at when planes hit the envelope.

Feature body becomes two apply calls:

1. Matching spec + Primary + Matching locations + keep the matching tool (Tap if Internal Primary, Die if External).
2. Complementary spec + Mating locations + keep the complementary tool.

Skip warnings stay list-level (`Internal N` / `External N` by thread type).

### 3. Delete the dead part-pick path

**Done when:** these are gone, including the `qNothing()` call in the feature body:

- `touchLegacyPartPickHelpers`, `solidsFromPartPicks`, `resolvePartPicks`, `resolvePartPicksAtHole`, `solidsOnHoleAxis`, `ownerSolidOf`, `pinAxialRange`, `holePinPlacement`, `expandPinStock`, `clipPinAtHoleEnd`
- Hidden `definition.boreParts` and `definition.boreMatingPart`

Leave `collidingSolids`, `radialExtentInCSys`, `placementTargets`. Leave unused finish helpers if they are not on that list (`applyInternalHoleFinish` may go unused once Primary is a tool apply).

### 4. Stop at

**Done when:** a plane that intersects the thread axis between the start and the end shortens the defined length (keep the side that contains the mid-point; if the hit is on the start side of mid, do not shift the origin — leave that plane to envelope clip). Every Stop at plane still runs through `clipThreadSweepAtStopPlanes` on the Primary cutter and on kept tools. Location instantiations do not take those planes as envelope clips. They inherit the shortened default length unless a Location End overrides.

### 5. Later (not this job)

Only after apply and builder exist. Do not start these in the same pass.

- One Finish bundle over `hole*` / `gen*` / `point*` aliases. Legacy keys stay hidden.
- Shared Matching/Mating precondition UI — FeatureScript annotations are compile-time; confirm a shared module is possible first.
- One sweep-clip module (`clipThreadSweepAtStopPlanes` vs `splitSweepKeepThread`). `clipPinAtHoleEnd` dies with step 3.

## Operation IDs to keep

| Op | Id |
|----|----|
| Primary boolean into Stock | `id + "cut"` |
| Matching location *n* | `id + "gen" + "t"\|"d" + n` |
| Mating location *n* | `id + "mate" + "t"\|"d" + n` |
| Kept Thread tap / Through tap | `id + "tap"` / `id + "throughTap"` |
| Kept Thread die / Through die | `id + "die"` / `id + "throughDie"` |

Build the Primary tool under `id + "primary"` so it does not steal `id + "cut"` from `createThreadedTap`’s own join.

## Map in today’s file

| What | Where |
|------|--------|
| `createHelicalGroove` | ~1884 |
| `createThreadedTap` / `createThreadedDie` | ~1975 / ~2282 |
| `tapSpecFromDefinition` / `dieSpecFromDefinition` | ~2507 / ~2532 |
| `cutTapLocations` / `cutDieLocations` | ~2581 / ~2624 |
| Four call sites | ~3445–3465 |
| Primary groove + finish | ~3347–3436 |
| Keep tools | ~3472–3519 |
| Legacy call | ~3403 (`qNothing()`) |
| Hidden part picks | ~2815–2823 |

## Regen

Tell the user to regen, in Onshape:

1. Cylinder Thread face, Internal and External.
2. Cone Thread face, Internal and External.
3. Four holes on one plate: Thread face plus three Matching locations on the same body.
4. One Mating location (complementary type, Thread clearance).
5. Keep Thread tap and Thread die; Through tap/die if those forms are on.
6. External pin in a corner with Stop at on the walls; Matching pins in the open must keep full height.
7. Die applied at a point on a plate: plate reduces to a pin.
8. Clock on one Location: phase offset from the Primary thread, not a world-zero reset.
9. Several pins on one body: Matching Locations are the pin walls or pin or boss end faces; sibling pins must survive. Die radius comes from that face, not the plate.
10. Along face on a cylindrical Location uses that face’s height; a pin end face still uses the defined Primary length unless another End is set.
