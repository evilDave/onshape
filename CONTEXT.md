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

**Primary thread**:
The helical groove cut into the owner of the thread face.
_Avoid_: initial thread

**Tap**:
The tool whose helical groove matches an internal thread.

**Die**:
The tool whose helical groove matches an external thread.

**Internal thread**:
A thread whose material lies outward of the helix — a hole.
_Avoid_: female

**External thread**:
A thread whose material lies inward of the helix — a pin.
_Avoid_: male
