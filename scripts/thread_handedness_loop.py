#!/usr/bin/env python3
"""Red-capable loop: Opposite / Location Z must not change physical handedness.

Mirrors printableThread.fs helixXAtAxial, alignGeneratedPlacement, and
oppositeEndThreadFrame. Mechanical right-hand: looking along a fixed world
axis, the point turns clockwise as it advances along that axis.
opHelix clockwise = !leftHanded, in the generation frame.
"""

from __future__ import annotations

import math
import sys
from dataclasses import dataclass


@dataclass(frozen=True)
class Frame:
    origin: tuple[float, float, float]
    x: tuple[float, float, float]
    z: tuple[float, float, float]


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def norm(a):
    return math.sqrt(dot(a, a))


def normalize(a):
    n = norm(a)
    if n == 0:
        raise ValueError("zero vector")
    return scale(a, 1.0 / n)


def rotate_about(axis, angle, vec):
    axis = normalize(axis)
    c = math.cos(angle)
    s = math.sin(angle)
    return add(
        add(scale(vec, c), scale(cross(axis, vec), s)),
        scale(axis, dot(axis, vec) * (1.0 - c)),
    )


def helix_x_at_axial(generator: Frame, pitch: float, left_handed: bool, along: float):
    turn = (along / pitch) * 2.0 * math.pi
    angle = turn if left_handed else -turn
    return rotate_about(generator.z, angle, generator.x)


def helix_point(frame: Frame, left_handed: bool, pitch: float, radius: float, along: float):
    x = helix_x_at_axial(frame, pitch, left_handed, along)
    return add(add(frame.origin, scale(frame.z, along)), scale(x, radius))


def physical_winding(frame: Frame, left_handed: bool, pitch: float, world_axis) -> float:
    """Winding about world_axis as position along world_axis increases.

    Negative is mechanical right-hand (clockwise looking along world_axis).
    Radial is from the thread axis (the line through frame.origin along frame.z).
    """
    axis = normalize(world_axis)
    samples = []
    for i in range(16):
        along = pitch * (i / 16.0)
        p = helix_point(frame, left_handed, pitch, 1.0, along)
        from_origin = sub(p, frame.origin)
        axial = dot(from_origin, axis)
        radial = sub(from_origin, scale(axis, axial))
        samples.append((axial, radial))
    samples.sort(key=lambda item: item[0])
    winding = 0.0
    for i in range(1, len(samples)):
        winding += dot(cross(samples[i - 1][1], samples[i][1]), axis)
    return winding


def is_right_handed(frame: Frame, left_handed: bool, pitch: float, world_axis) -> bool:
    return physical_winding(frame, left_handed, pitch, world_axis) < 0


def align_generated_placement(spec_left: bool, generator: Frame, located: Frame, pitch: float, flip_when_antiparallel: bool):
    along = dot(sub(located.origin, generator.origin), generator.z)
    x = helix_x_at_axial(generator, pitch, spec_left, along)
    z = located.z
    x = sub(x, scale(z, dot(x, z)))
    x = normalize(x)
    flipped = dot(z, generator.z) < 0
    left = ((not spec_left) if flipped else spec_left) if flip_when_antiparallel else spec_left
    return Frame(located.origin, x, z), left


def opposite_end_frame(generator: Frame, length: float, pitch: float, left: bool, invert_handed: bool):
    origin = add(generator.origin, scale(generator.z, length))
    z = scale(generator.z, -1.0)
    x = helix_x_at_axial(generator, pitch, left, length)
    x = sub(x, scale(z, dot(x, z)))
    x = normalize(x)
    return Frame(origin, x, z), (not left) if invert_handed else left


def check(name: str, ok: bool, failures: list[str]):
    status = "PASS" if ok else "FAIL"
    print(f"[{status}] {name}")
    if not ok:
        failures.append(name)


def main() -> int:
    pitch = 10.0
    length = 20.0
    world_z = (0.0, 0.0, 1.0)
    primary = Frame((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0))
    # Primary Opposite: threadSurfaceFrame flips Z and keeps leftHanded.
    primary_flipped = Frame((0.0, 0.0, length), (1.0, 0.0, 0.0), (0.0, 0.0, -1.0))
    # Point / mate: default invert of an evAxis that matched the face +Z.
    point_default = Frame((0.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, -1.0))
    face_antiparallel = Frame((30.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, -1.0))
    face_parallel = Frame((30.0, 0.0, 0.0), (1.0, 0.0, 0.0), (0.0, 0.0, 1.0))

    failures: list[str] = []

    check("primary RH stays RH", is_right_handed(primary, False, pitch, world_z), failures)
    check(
        "primary Opposite (Z flip, same leftHanded) stays RH",
        is_right_handed(primary_flipped, False, pitch, world_z),
        failures,
    )
    check("definition leftHanded is physically LH", not is_right_handed(primary, True, pitch, world_z), failures)

    # FeatureScript: leftHanded is the definition. Location Z / Opposite must not invert it.
    cases = [
        ("point/mate vs primary", False, primary, point_default),
        ("antiparallel matching face", False, primary, face_antiparallel),
        ("parallel matching face", False, primary, face_parallel),
        ("point after primary Opposite", False, primary_flipped, point_default),
        ("LH point/mate", True, primary, point_default),
    ]
    for name, spec_left, gen, loc in cases:
        frame, left = align_generated_placement(spec_left, gen, loc, pitch, False)
        want_rh = not spec_left
        got_rh = is_right_handed(frame, left, pitch, world_z)
        check(f"{name}: same handedness as definition", got_rh == want_rh, failures)

    die_frame, die_left = opposite_end_frame(primary, length, pitch, False, False)
    check(
        "kept-die opposite-end frame: same handedness as definition",
        is_right_handed(die_frame, die_left, pitch, world_z),
        failures,
    )

    if failures:
        print("\nRED: " + "; ".join(failures))
        return 1
    print("\nGREEN: handedness is invariant under Opposite and Location Z.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
