# Onshape features

Custom Onshape features for printable threads, compliant springs, and part filtering.

## Printable Thread

**Thread face**:
The selected cylindrical or conical face that generates the thread.
_Avoid_: surface

**Helical groove**:
The swept triangular solid that forms the thread. The primary cut, tap, and die share one helical groove.
_Avoid_: thread cutter, thread form

**Thread form**:
The 2D triangular profile swept along the helix.
_Avoid_: profile

**Wall angle**:
The flank angle of the Thread form, in a plane containing the thread axis (the likely print direction).
_Avoid_: flank angle, print angle

**Primary thread**:
The thread on the owner of the Thread face, whose axial extent is that face (the cylinder ends, or the base and top of the cone section) unless Stop at planes cross the axis. The matching Tap or Die applied to the same geometry is this thread.
_Avoid_: initial thread

**Tap**:
The tool whose helical groove matches an Internal thread. It produces a hole or uses an already present hole. The Tap that cuts a Location and the kept Tap are the same tool.
_Avoid_: internal cutter

**Die**:
The tool whose helical groove matches an External thread. It always cuts a pin from a boss; on a plate a vertex or mate Location reduces the plate until only a pin remains, and a face Location cuts only that face. The Die that cuts a Location and the kept Die are the same tool.
_Avoid_: external cutter

**Internal thread**:
A thread whose material lies outward of the helix — a hole.
_Avoid_: female

**External thread**:
A thread whose material lies inward of the helix — a pin.
_Avoid_: male

**Location**:
A sketch vertex, mate connector, cylindrical or conical face, or pin or boss end face that names an axis for another thread cut. A Location cuts exactly one body and owns Clock, Opposite, End, and Finish; if it matches both the owner of the Thread face and one other part, the cut is the other part. How the tool behaves depends only on Tap versus Die, not on Matching versus Mating. A face Location bounds a Die to that face.
_Avoid_: bore, generated point, hole, surface

**Matching location**:
A Location that receives the same type as the Primary thread, with no Thread clearance. A label for which list, not a different kind of tool.
_Avoid_: initial location, generated location, initial thread

**Mating location**:
A Location that receives the complementary type to the Primary thread, with Thread clearance. A label for which list, not a different kind of tool.
_Avoid_: bore location

**Pin end face**:
The planar face at the end of a pin or boss. A Location may be this face; it names the axis and the Die radius.
_Avoid_: end cap, boss face

**Thread clearance**:
The radial offset on the complementary Tap or Die so a Mating location fits the Primary thread. A Location cannot override it; it is part of that tool.
_Avoid_: gap, tolerance

**Clock**:
An angular offset of helix phase at a Location, relative to the Primary thread.
_Avoid_: rotation, twist

**Opposite**:
Which end of the axis is the start of the thread. The direction arrow points to the end (end chamfer on an External thread, counterbore on an Internal thread); its base is the start (start chamfer on an Internal thread). On a Location it is which way the tool is applied, and so the achieved thread direction.
_Avoid_: flip, reverse, direction

**Finish**:
The non-thread treatment at each end: start chamfer at the start, and counterbore (Internal) or end chamfer (External) at the end. Defaults come from the Primary thread; a Location may override.
_Avoid_: hole finish, die chamfer

**Through tap**:
The same Tap split into printable parts (chamfer, thread, counterbore). Through length is the threaded part between chamfer and counterbore if they exist.
_Avoid_: without counterbore

**Through die**:
The same Die split into printable parts (thread, inverse-cone chamfer). Through length is the threaded part between those parts if they exist.
_Avoid_: without chamfer

**End**:
The axial bound of a Location's thread: Blind, Through all, Up to next, Up to face, Up to part, Up to vertex, or Along face (the Location face's own axial height).
_Avoid_: stop at, draft

**Stop at**:
Planes that bound the Primary thread. Where they cross the axis they shorten that thread and the default Tap and Die; where they only hit the tool envelope they clip the Primary thread and the kept tools, not a Location tool's envelope.
_Avoid_: end

**Stock**:
The material the feature cuts, including the owner of the Thread face.
_Avoid_: target, part (for this role)
