FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/chamfertype.gen.fs", version : "3070.0");
printableThreadIcon::import(path : "0bcd13a2640f002407a4b1c6", version : "3adc2e2d6ab96f6647b27cd3");

/**
 * Cuts a triangular helical groove into a cylindrical or conical face.
 * Wall angle is measured in a plane containing the axis (the print
 * direction), from the plane perpendicular to the axis; 45° prints
 * without support when the thread axis is vertical.
 */

const DIRECTION_MANIPULATOR = "directionManipulator";

const PRINTABLE_THREAD_PITCH_BOUNDS =
{
    (millimeter) : [0.2, 10, 100],
    (centimeter) : 0.2,
    (meter) : 0.002,
    (inch) : 0.08
} as LengthBoundSpec;

const PRINTABLE_THREAD_DEPTH_BOUNDS =
{
    (millimeter) : [0.05, 4, 50],
    (centimeter) : 0.4,
    (meter) : 0.004,
    (inch) : 0.16
} as LengthBoundSpec;

const PRINTABLE_THREAD_TRUNCATION_BOUNDS =
{
    (millimeter) : [0, 0.6, 20],
    (centimeter) : 0.06,
    (meter) : 0.0006,
    (inch) : 0.024
} as LengthBoundSpec;

const PRINTABLE_THREAD_FLAT_BOUNDS =
{
    (millimeter) : [0.02, 1.4, 80],
    (centimeter) : 0.14,
    (meter) : 0.0014,
    (inch) : 0.055
} as LengthBoundSpec;

const PRINTABLE_THREAD_WALL_ANGLE_BOUNDS =
{
    (degree) : [15, 45, 75],
    (radian) : PI / 4
} as AngleBoundSpec;

const PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS =
{
    (millimeter) : [0.1, 2, 50],
    (centimeter) : 0.2,
    (meter) : 0.002,
    (inch) : 0.08
} as LengthBoundSpec;

const PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS =
{
    (degree) : [5, 45, 85],
    (radian) : PI / 4
} as AngleBoundSpec;

const PRINTABLE_THREAD_CLEARANCE_BOUNDS =
{
    (millimeter) : [0, 0.2, 10],
    (centimeter) : 0.02,
    (meter) : 0.0002,
    (inch) : 0.008
} as LengthBoundSpec;

const PRINTABLE_THREAD_COUNTERBORE_DEEPER_BOUNDS =
{
    (millimeter) : [0, 1, 50],
    (centimeter) : 0.1,
    (meter) : 0.001,
    (inch) : 0.04
} as LengthBoundSpec;

const PRINTABLE_THREAD_COUNTERBORE_BACK_BOUNDS =
{
    (millimeter) : [0, 2, 50],
    (centimeter) : 0.2,
    (meter) : 0.002,
    (inch) : 0.08
} as LengthBoundSpec;

const PRINTABLE_THREAD_TAP_LENGTH_BOUNDS =
{
    (millimeter) : [1, 50, 500],
    (centimeter) : 5,
    (meter) : 0.05,
    (inch) : 2
} as LengthBoundSpec;

const PRINTABLE_THREAD_CLOCK_BOUNDS =
{
    (degree) : [0, 0, 3600],
    (radian) : 0
} as AngleBoundSpec;

const PRINTABLE_THREAD_DIE_OUTER_BOUNDS =
{
    (millimeter) : [2, 20, 500],
    (centimeter) : 2,
    (meter) : 0.02,
    (inch) : 0.8
} as LengthBoundSpec;

export enum BoreEndType
{
    annotation { "Name" : "Blind" }
    BLIND,
    annotation { "Name" : "Through all" }
    THROUGH_ALL,
    annotation { "Name" : "Up to next" }
    UP_TO_NEXT,
    annotation { "Name" : "Up to face" }
    UP_TO_FACE,
    annotation { "Name" : "Up to part" }
    UP_TO_PART,
    annotation { "Name" : "Up to vertex" }
    UP_TO_VERTEX
}

export enum KeptTapForm
{
    annotation { "Name" : "Thread tap" }
    WITH_COUNTERBORE,
    annotation { "Name" : "Through tap" }
    WITHOUT_COUNTERBORE,
    annotation { "Name" : "Both" }
    BOTH
}

export enum KeptDieForm
{
    annotation { "Name" : "Thread die" }
    THREAD,
    annotation { "Name" : "Through die" }
    THROUGH,
    annotation { "Name" : "Both" }
    BOTH
}

export enum ClockSense
{
    annotation { "Name" : "Clockwise" }
    CLOCKWISE,
    annotation { "Name" : "Anticlockwise" }
    ANTICLOCKWISE
}

export enum ThreadDependentParameter
{
    annotation { "Name" : "Pitch" }
    PITCH,
    annotation { "Name" : "Thread depth" }
    DEPTH,
    annotation { "Name" : "Truncation" }
    TRUNCATION,
    annotation { "Name" : "Flat length" }
    FLAT
}

function requireThreadFace(context is Context, faceQuery is Query) returns Query
{
    const faces = evaluateQuery(context, faceQuery);
    if (size(faces) != 1)
    {
        throw regenError("Select exactly one cylindrical or conical face.", ["surface"]);
    }

    const face = faces[0];
    if (isQueryEmpty(context, qBodyType(qOwnerBody(face), BodyType.SOLID)))
    {
        throw regenError("Select a face on a solid part.", ["surface"]);
    }
    return face;
}

function threadSurfaceFrame(context is Context, face is Query, oppositeDirection is boolean) returns map
{
    var surface;
    try silent
    {
        surface = evSurfaceDefinition(context, { "face" : face });
    }
    catch
    {
        throw regenError("Select a cylindrical or conical face.", ["surface"]);
    }

    if (!(surface is Cylinder) && !(surface is Cone))
    {
        throw regenError("Select a cylindrical or conical face.", ["surface"]);
    }

    var localCoordSys = surface.coordSystem;
    const bounds = evBox3d(context, {
                "topology" : face,
                "cSys" : localCoordSys,
                "tight" : true
            });
    const height = bounds.maxCorner[2] - bounds.minCorner[2];
    if (height <= TOLERANCE.zeroLength * meter)
    {
        throw regenError("The selected face has no length along its axis.", ["surface"]);
    }

    localCoordSys.origin += bounds.minCorner[2] * localCoordSys.zAxis;

    var startRadius;
    var endRadius;
    if (surface is Cylinder)
    {
        startRadius = surface.radius;
        endRadius = surface.radius;
    }
    else
    {
        const minZ = max(0 * meter, bounds.minCorner[2]);
        startRadius = tan(surface.halfAngle) * minZ;
        endRadius = tan(surface.halfAngle) * bounds.maxCorner[2];
    }

    if (oppositeDirection)
    {
        const swappedRadius = startRadius;
        startRadius = endRadius;
        endRadius = swappedRadius;
        localCoordSys.zAxis = -localCoordSys.zAxis;
        localCoordSys.origin -= height * localCoordSys.zAxis;
    }

    return {
            "localCoordSys" : localCoordSys,
            "startRadius" : startRadius,
            "endRadius" : endRadius,
            "height" : height
        };
}

function radialAwayFromAxis(localCoordSys is CoordSystem, surfacePoint is Vector) returns Vector
{
    const axial = localCoordSys.zAxis;
    var fromAxis = surfacePoint - localCoordSys.origin;
    fromAxis = fromAxis - axial * dot(fromAxis, axial);
    if (norm(fromAxis) <= TOLERANCE.zeroLength * meter)
    {
        throw regenError("Could not determine a cut direction for the selected face.", ["surface"]);
    }
    return normalize(fromAxis);
}

function threadCutsInward(context is Context, face is Query, localCoordSys is CoordSystem, surfacePoint is Vector) returns boolean
{
    const awayFromAxis = radialAwayFromAxis(localCoordSys, surfacePoint);
    const closest = evDistance(context, {
                "side0" : face,
                "side1" : surfacePoint
            });
    const sample = evFaceTangentPlane(context, {
                "face" : face,
                "parameter" : closest.sides[0].parameter
            });

    // External faces have an outward normal; internal faces point the other way.
    return dot(sample.normal, awayFromAxis) > 0;
}

function intoMaterialDirection(localCoordSys is CoordSystem, surfacePoint is Vector, cutsInward is boolean) returns Vector
{
    const awayFromAxis = radialAwayFromAxis(localCoordSys, surfacePoint);
    return cutsInward ? -awayFromAxis : awayFromAxis;
}

function detectPrimaryIsInternal(context is Context, faceQuery is Query) returns boolean
{
    if (!(faceQuery is Query) || isQueryEmpty(context, faceQuery))
    {
        return false;
    }
    const faces = evaluateQuery(context, faceQuery);
    if (size(faces) != 1)
    {
        return false;
    }
    const face = faces[0];
    const frame = try silent(threadSurfaceFrame(context, face, false));
    if (!(frame is map))
    {
        return false;
    }
    const startPoint = toWorld(frame.localCoordSys, vector(frame.startRadius, 0 * meter, 0 * meter));
    return !threadCutsInward(context, face, frame.localCoordSys, startPoint);
}

function directedEndEdges(context is Context, face is Query, localCoordSys is CoordSystem, height is ValueWithUnits) returns Query
{
    const zTolerance = 0.01 * millimeter;
    var edges = [];
    for (var edge in evaluateQuery(context, qAdjacent(face, AdjacencyType.EDGE, EntityType.EDGE)))
    {
        const edgeBox = evBox3d(context, {
                    "topology" : edge,
                    "cSys" : localCoordSys,
                    "tight" : true
                });
        if (abs(edgeBox.minCorner[2] - height) <= zTolerance && abs(edgeBox.maxCorner[2] - height) <= zTolerance)
        {
            edges = append(edges, edge);
        }
    }
    if (size(edges) == 0)
    {
        throw regenError("Could not find an edge at the directed end to chamfer.", ["chamferEnd"]);
    }
    return qUnion(edges);
}

function chamferWidthOnFace(context is Context, edge is Query, threadFace is Query) returns boolean
{
    const faces = evaluateQuery(context, qAdjacent(edge, AdjacencyType.EDGE, EntityType.FACE));
    if (size(faces) == 0)
    {
        return false;
    }
    return isQueryEmpty(context, qIntersection([faces[0], threadFace]));
}

function threadRadiusAt(startRadius is ValueWithUnits, endRadius is ValueWithUnits, height is ValueWithUnits, axial is ValueWithUnits, clearance is ValueWithUnits) returns ValueWithUnits
{
    return max(startRadius + (endRadius - startRadius) * (axial / height) + clearance, 0.05 * millimeter);
}

function threadDependentParameter(definition is map) returns ThreadDependentParameter
{
    return definition.dependentParameter is ThreadDependentParameter ? definition.dependentParameter : ThreadDependentParameter.FLAT;
}

function threadLengthValue(definition is map, field is string, calculatedField is string, fallback is ValueWithUnits) returns ValueWithUnits
{
    if (definition[field] is ValueWithUnits)
    {
        return definition[field];
    }
    if (definition[calculatedField] is ValueWithUnits)
    {
        return definition[calculatedField];
    }
    return fallback;
}

function threadProfileDimensions(definition is map) returns map
{
    const wallAngle = definition.wallAngle is ValueWithUnits ? definition.wallAngle : 45 * degree;
    var pitch = threadLengthValue(definition, "pitch", "pitchCalculated", 10 * millimeter);
    var depth = threadLengthValue(definition, "depth", "depthCalculated", 4 * millimeter);
    var truncation = threadLengthValue(definition, "truncation", "truncationCalculated", 0.6 * millimeter);
    var flatLength = threadLengthValue(definition, "flatLength", "flatLengthCalculated", 1.4 * millimeter);
    const dependent = threadDependentParameter(definition);
    const slope = tan(wallAngle);

    if (dependent == ThreadDependentParameter.PITCH)
    {
        pitch = flatLength + truncation + 2 * depth / slope;
    }
    else if (dependent == ThreadDependentParameter.DEPTH)
    {
        depth = (pitch - flatLength - truncation) * slope / 2;
    }
    else if (dependent == ThreadDependentParameter.TRUNCATION)
    {
        truncation = pitch - flatLength - 2 * depth / slope;
    }
    else
    {
        flatLength = pitch - truncation - 2 * depth / slope;
    }

    return {
            "pitch" : pitch,
            "depth" : depth,
            "truncation" : truncation,
            "flatLength" : flatLength,
            "wallAngle" : wallAngle,
            "dependentParameter" : dependent
        };
}

function applyDependentThreadDimension(definition is map) returns map
{
    const dims = threadProfileDimensions(definition);
    if (dims.dependentParameter == ThreadDependentParameter.PITCH)
    {
        definition.pitch = dims.pitch;
        definition.pitchCalculated = dims.pitch;
    }
    else if (dims.dependentParameter == ThreadDependentParameter.DEPTH)
    {
        definition.depth = dims.depth;
        definition.depthCalculated = dims.depth;
    }
    else if (dims.dependentParameter == ThreadDependentParameter.TRUNCATION)
    {
        definition.truncation = dims.truncation;
        definition.truncationCalculated = dims.truncation;
    }
    else
    {
        definition.flatLength = dims.flatLength;
        definition.flatLengthCalculated = dims.flatLength;
    }
    return definition;
}

function specifiedThreadFields(dependent is ThreadDependentParameter) returns array
{
    var fields = ["wallAngle"];
    if (dependent != ThreadDependentParameter.PITCH)
    {
        fields = append(fields, "pitch");
    }
    if (dependent != ThreadDependentParameter.DEPTH)
    {
        fields = append(fields, "depth");
    }
    if (dependent != ThreadDependentParameter.TRUNCATION)
    {
        fields = append(fields, "truncation");
    }
    if (dependent != ThreadDependentParameter.FLAT)
    {
        fields = append(fields, "flatLength");
    }
    return fields;
}

function threadProfilePlane(origin is Vector, intoMaterial is Vector, axial is Vector) returns Plane
{
    return plane(origin, cross(intoMaterial, axial), intoMaterial);
}

function threadProfileHalfWidths(overlap is ValueWithUnits, depth is ValueWithUnits, truncation is ValueWithUnits, wallAngle is ValueWithUnits) returns map
{
    const rootHalfWidth = truncation / 2;
    const outerHalfWidth = rootHalfWidth + (depth + overlap) / tan(wallAngle);
    return {
            "rootHalfWidth" : rootHalfWidth,
            "outerHalfWidth" : outerHalfWidth
        };
}

function threadProfilePoints(overlap is ValueWithUnits, depth is ValueWithUnits, outerHalfWidth is ValueWithUnits, rootHalfWidth is ValueWithUnits, truncation is ValueWithUnits) returns array
{
    if (truncation > TOLERANCE.zeroLength * meter)
    {
        return [
            vector(-overlap, -outerHalfWidth),
            vector(depth, -rootHalfWidth),
            vector(depth, rootHalfWidth),
            vector(-overlap, outerHalfWidth),
            vector(-overlap, -outerHalfWidth)
        ];
    }
    return [
        vector(-overlap, -outerHalfWidth),
        vector(depth, 0 * meter),
        vector(-overlap, outerHalfWidth),
        vector(-overlap, -outerHalfWidth)
    ];
}

function revolveAxisProfile(context is Context, id is Id, localCoordSys is CoordSystem, points is array) returns Query
{
    const sketchPlane = plane(localCoordSys.origin, cross(localCoordSys.xAxis, localCoordSys.zAxis), localCoordSys.xAxis);
    const sketch = newSketchOnPlane(context, id + "sketch", {
                "sketchPlane" : sketchPlane
            });
    skPolyline(sketch, "profile", {
                "points" : points
            });
    skSolve(sketch);
    opRevolve(context, id + "body", {
                "entities" : qSketchRegion(id + "sketch"),
                "axis" : line(localCoordSys.origin, localCoordSys.zAxis),
                "angleForward" : 360 * degree
            });
    opDeleteBodies(context, id + "deleteSketch", {
                "entities" : qCreatedBy(id + "sketch", EntityType.BODY)
            });
    return qCreatedBy(id + "body", EntityType.BODY);
}

function createTapStock(context is Context, id is Id, localCoordSys is CoordSystem, startRadius is ValueWithUnits, endRadius is ValueWithUnits, height is ValueWithUnits, tapLength is ValueWithUnits, radialClearance is ValueWithUnits) returns Query
{
    const r0 = threadRadiusAt(startRadius, endRadius, height, 0 * meter, radialClearance);
    const r1 = threadRadiusAt(startRadius, endRadius, height, tapLength, radialClearance);
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(r0, 0 * meter),
                vector(r1, tapLength),
                vector(0 * meter, tapLength),
                vector(0 * meter, 0 * meter),
                vector(r0, 0 * meter)
            ]);
}

function ownerSolidOf(context is Context, selection is Query) returns Query
{
    var owner = qBodyType(selection, BodyType.SOLID);
    if (!isQueryEmpty(context, owner))
    {
        return qUnion(evaluateQuery(context, owner));
    }
    owner = qBodyType(qOwnerBody(selection), BodyType.SOLID);
    if (!isQueryEmpty(context, owner))
    {
        return qUnion(evaluateQuery(context, owner));
    }
    return qNothing();
}

function solidsOnHoleAxis(context is Context, exclude is Query, holeCSys is CoordSystem, holeRadius is ValueWithUnits) returns Query
{
    const axis = line(holeCSys.origin, holeCSys.zAxis);
    var pins = [];
    for (var solid in evaluateQuery(context, qSubtraction(qAllModifiableSolidBodies(), exclude)))
    {
        const closest = evDistance(context, {
                    "side0" : solid,
                    "side1" : axis
                });
        if (closest.distance <= holeRadius + 0.5 * millimeter)
        {
            pins = append(pins, solid);
        }
    }
    if (size(pins) == 0)
    {
        return qNothing();
    }
    return qUnion(pins);
}

function solidsFromPartPicks(context is Context, picks is Query) returns Query
{
    return resolvePartPicks(context, picks, qNothing());
}

function resolvePartPicks(context is Context, picks is Query, exclude is Query) returns Query
{
    return resolvePartPicksAtHole(context, picks, exclude, coordSystem(vector(0, 0, 0) * meter, vector(1, 0, 0), vector(0, 0, 1)), -1 * meter);
}

function resolvePartPicksAtHole(context is Context, picks is Query, exclude is Query, holeCSys is CoordSystem, holeRadius is ValueWithUnits) returns Query
{
    if (!(picks is Query) || isQueryEmpty(context, picks))
    {
        return qNothing();
    }
    var solids = [];
    for (var selection in evaluateQuery(context, picks))
    {
        var owner = ownerSolidOf(context, selection);
        if (!isQueryEmpty(context, exclude) && !isQueryEmpty(context, qIntersection([owner, exclude])))
        {
            owner = qNothing();
            const others = collidingSolids(context, qOwnerBody(selection), qSubtraction(qAllModifiableSolidBodies(), exclude));
            if (!isQueryEmpty(context, others))
            {
                owner = others;
            }
            else if (holeRadius > 0 * meter)
            {
                owner = solidsOnHoleAxis(context, exclude, holeCSys, holeRadius);
            }
        }
        if (!isQueryEmpty(context, owner))
        {
            for (var body in evaluateQuery(context, owner))
            {
                solids = append(solids, body);
            }
        }
    }
    if (size(solids) == 0)
    {
        return qNothing();
    }
    return qUnion(evaluateQuery(context, qUnion(solids)));
}

function radialExtentInCSys(context is Context, bodies is Query, cSys is CoordSystem) returns ValueWithUnits
{
    var extent = 0 * meter;
    for (var body in evaluateQuery(context, bodies))
    {
        const boxResult = evBox3d(context, {
                    "topology" : body,
                    "cSys" : cSys,
                    "tight" : true
                });
        const x = max(abs(boxResult.minCorner[0]), abs(boxResult.maxCorner[0]));
        const y = max(abs(boxResult.minCorner[1]), abs(boxResult.maxCorner[1]));
        extent = max(extent, sqrt(x * x + y * y));
    }
    return extent;
}

function pinAxialRange(context is Context, pin is Query, cSys is CoordSystem) returns map
{
    var zLo = 0 * meter;
    var zHi = 0 * meter;
    var has = false;
    for (var vertex in evaluateQuery(context, qOwnedByBody(pin, EntityType.VERTEX)))
    {
        const point = evVertexPoint(context, {
                    "vertex" : vertex
                });
        const z = fromWorld(cSys, point)[2];
        if (!has)
        {
            zLo = z;
            zHi = z;
            has = true;
        }
        else
        {
            zLo = min(zLo, z);
            zHi = max(zHi, z);
        }
    }
    const boxResult = evBox3d(context, {
                "topology" : pin,
                "cSys" : cSys,
                "tight" : false
            });
    if (!has)
    {
        zLo = boxResult.minCorner[2];
        zHi = boxResult.maxCorner[2];
    }
    else
    {
        zLo = min(zLo, boxResult.minCorner[2]);
        zHi = max(zHi, boxResult.maxCorner[2]);
    }
    return {
            "zLo" : zLo,
            "zHi" : zHi,
            "length" : zHi - zLo
        };
}

function holePinPlacement(context is Context, pin is Query, holeCSys is CoordSystem, height is ValueWithUnits) returns map
{
    const range = pinAxialRange(context, pin, holeCSys);
    var zLo = range.zLo;
    var zHi = range.zHi;
    var pinLength = range.length;
    if (pinLength <= 0.05 * millimeter)
    {
        const base = holeCSys.origin + height * holeCSys.zAxis;
        const closest = evDistance(context, {
                    "side0" : pin,
                    "side1" : base
                });
        const tip = closest.sides[0].point;
        const along = dot(tip - holeCSys.origin, holeCSys.zAxis);
        zLo = along;
        zHi = along + height;
        pinLength = height;
    }

    const overlapStart = max(zLo, 0 * meter);
    const overlapEnd = min(zHi, height);
    const insertion = overlapEnd - overlapStart;
    const tipIsHigh = abs(zHi - height) <= abs(zLo - height);
    var originZ;
    var axis = holeCSys.zAxis;
    var dieLength;

    if (insertion > 0.05 * millimeter)
    {
        dieLength = insertion;
        if (tipIsHigh)
        {
            originZ = overlapEnd;
            axis = -axis;
        }
        else
        {
            originZ = overlapStart;
        }
    }
    else
    {
        dieLength = pinLength;
        if (tipIsHigh)
        {
            originZ = zHi;
            axis = -axis;
        }
        else
        {
            originZ = zLo;
        }
    }

    return {
            "coordSystem" : coordSystem(holeCSys.origin + originZ * holeCSys.zAxis, holeCSys.xAxis, axis),
            "dieLength" : dieLength
        };
}

function expandPinStock(context is Context, id is Id, pin is Query, localCoordSys is CoordSystem, dieLength is ValueWithUnits, minRadius is ValueWithUnits) returns Query
{
    const overlap = 0.05 * millimeter;
    const stock = revolveAxisProfile(context, id + "stock", localCoordSys, [
                vector(0 * meter, -overlap),
                vector(minRadius, -overlap),
                vector(minRadius, dieLength + overlap),
                vector(0 * meter, dieLength + overlap),
                vector(0 * meter, -overlap)
            ]);
    try
    {
        opBoolean(context, id + "union", {
                    "tools" : qUnion([pin, stock]),
                    "operationType" : BooleanOperationType.UNION
                });
    }
    catch
    {
        opDeleteBodies(context, id + "deleteStock", {
                    "entities" : stock
                });
        return pin;
    }
    const grown = qCreatedBy(id + "union", EntityType.BODY);
    if (!isQueryEmpty(context, grown))
    {
        return grown;
    }
    return pin;
}

function clipPinAtHoleEnd(context is Context, id is Id, pin is Query, holeCSys is CoordSystem, height is ValueWithUnits) returns Query
{
    const range = pinAxialRange(context, pin, holeCSys);
    if (range.zHi <= height + 0.05 * millimeter)
    {
        return pin;
    }
    return splitSweepKeepThread(context, id, pin, plane(holeCSys.origin + height * holeCSys.zAxis, holeCSys.zAxis), holeCSys, height);
}

function touchLegacyPartPickHelpers(context is Context, id is Id, pin is Query, holeCSys is CoordSystem, height is ValueWithUnits)
{
    if (!isQueryEmpty(context, pin))
    {
        const solids = solidsFromPartPicks(context, pin);
        if (!isQueryEmpty(context, solids))
        {
            const placed = holePinPlacement(context, solids, holeCSys, height);
            const grown = expandPinStock(context, id + "legacyGrow", solids, placed.coordSystem, placed.dieLength, 1 * millimeter);
            clipPinAtHoleEnd(context, id + "legacyClip", grown, holeCSys, height);
        }
    }
}

function collidingSolids(context is Context, tool is Query, targets is Query) returns Query
{
    if (isQueryEmpty(context, targets))
    {
        return qNothing();
    }
    var hits = [];
    for (var clash in evCollision(context, {
                    "tools" : tool,
                    "targets" : targets
                }))
    {
        hits = append(hits, clash.targetBody);
    }
    return qUnion(evaluateQuery(context, qUnion(hits)));
}

function planeFromStopPick(context is Context, entity is Query) returns map
{
    const fromFace = try silent(evPlane(context, {
                    "face" : entity
                }));
    if (fromFace is Plane)
    {
        return { "plane" : fromFace };
    }
    const mate = try silent(evMateConnector(context, {
                    "mateConnector" : entity
                }));
    if (mate is CoordSystem)
    {
        return { "plane" : plane(mate.origin, mate.zAxis) };
    }
    return {};
}

function sweepBoxInCSys(context is Context, body is Query, cSys is CoordSystem) returns map
{
    if (isQueryEmpty(context, body))
    {
        return {};
    }
    return {
            "extent" : evBox3d(context, {
                        "topology" : body,
                        "cSys" : cSys,
                        "tight" : true
                    })
        };
}

function sweepSpanContainsZ(extent is Box3d, z is ValueWithUnits) returns boolean
{
    return extent.minCorner[2] <= z && extent.maxCorner[2] >= z;
}

function sweepOverlapWithThread(extent is Box3d, height is ValueWithUnits) returns ValueWithUnits
{
    const lo = max(extent.minCorner[2], 0 * meter);
    const hi = min(extent.maxCorner[2], height);
    return max(hi - lo, 0 * meter);
}

function sweepBoxCenter(extent is Box3d, cSys is CoordSystem) returns Vector
{
    return toWorld(cSys, 0.5 * (extent.minCorner + extent.maxCorner));
}

function pickThreadedSweepSide(context is Context, front is Query, back is Query, localCoordSys is CoordSystem, height is ValueWithUnits, keepPoint is Vector) returns map
{
    const midZ = height / 2;
    const frontFound = sweepBoxInCSys(context, front, localCoordSys);
    const backFound = sweepBoxInCSys(context, back, localCoordSys);
    const frontOk = frontFound.extent is Box3d;
    const backOk = backFound.extent is Box3d;
    if (frontOk && !backOk)
    {
        return { "keep" : front, "discard" : back };
    }
    if (backOk && !frontOk)
    {
        return { "keep" : back, "discard" : front };
    }
    if (!frontOk && !backOk)
    {
        return {};
    }

    const frontBox = frontFound.extent;
    const backBox = backFound.extent;
    const frontHasMid = sweepSpanContainsZ(frontBox, midZ);
    const backHasMid = sweepSpanContainsZ(backBox, midZ);
    if (frontHasMid && !backHasMid)
    {
        return { "keep" : front, "discard" : back };
    }
    if (backHasMid && !frontHasMid)
    {
        return { "keep" : back, "discard" : front };
    }

    const frontOverlap = sweepOverlapWithThread(frontBox, height);
    const backOverlap = sweepOverlapWithThread(backBox, height);
    if (frontOverlap > backOverlap + TOLERANCE.zeroLength * meter)
    {
        return { "keep" : front, "discard" : back };
    }
    if (backOverlap > frontOverlap + TOLERANCE.zeroLength * meter)
    {
        return { "keep" : back, "discard" : front };
    }

    const frontDist = norm(sweepBoxCenter(frontBox, localCoordSys) - keepPoint);
    const backDist = norm(sweepBoxCenter(backBox, localCoordSys) - keepPoint);
    if (frontDist <= backDist)
    {
        return { "keep" : front, "discard" : back };
    }
    return { "keep" : back, "discard" : front };
}

function clipThreadSweepAtStopPlanes(context is Context, id is Id, sweep is Query, stopAt is Query, localCoordSys is CoordSystem, height is ValueWithUnits) returns Query
{
    if (!(stopAt is Query) || isQueryEmpty(context, stopAt) || isQueryEmpty(context, sweep))
    {
        return sweep;
    }

    const keepPoint = localCoordSys.origin + height / 2 * localCoordSys.zAxis;
    var remaining = qOwnerBody(sweep);
    var missed = 0;
    var opIndex = 0;
    for (var entity in evaluateQuery(context, stopAt))
    {
        const picked = planeFromStopPick(context, entity);
        if (!(picked.plane is Plane))
        {
            missed += 1;
            continue;
        }
        const cutPlane = picked.plane;

        const boxResult = evBox3d(context, {
                    "topology" : remaining,
                    "cSys" : planeToCSys(cutPlane),
                    "tight" : true
                });
        const entirelyBehind = boxResult.maxCorner[2] < TOLERANCE.zeroLength * meter;
        const entirelyInFront = boxResult.minCorner[2] > -TOLERANCE.zeroLength * meter;
        if (entirelyBehind || entirelyInFront)
        {
            missed += 1;
            continue;
        }

        const stepId = id + "stop" + opIndex;
        const planeId = stepId + "plane";
        const splitId = stepId + "split";
        opPlane(context, planeId, {
                    "plane" : cutPlane
                });
        const planeTool = qOwnerBody(qCreatedBy(planeId));
        try
        {
            opSplitPart(context, splitId, {
                        "targets" : remaining,
                        "tool" : planeTool,
                        "keepTools" : false
                    });
        }
        catch
        {
            if (!isQueryEmpty(context, planeTool))
            {
                opDeleteBodies(context, stepId + "planeDelete", {
                            "entities" : planeTool
                        });
            }
            missed += 1;
            opIndex += 1;
            continue;
        }

        const back = qSplitBy(splitId, EntityType.BODY, true);
        const front = qSplitBy(splitId, EntityType.BODY, false);
        const sides = pickThreadedSweepSide(context, front, back, localCoordSys, height, keepPoint);
        if (!(sides.keep is Query) || isQueryEmpty(context, sides.keep))
        {
            missed += 1;
            opIndex += 1;
            continue;
        }

        if (sides.discard is Query && !isQueryEmpty(context, sides.discard))
        {
            opDeleteBodies(context, stepId + "discard", {
                        "entities" : sides.discard
                    });
        }
        remaining = sides.keep;
        opIndex += 1;
    }

    if (missed == 1)
    {
        reportFeatureWarning(context, id, "A stop selection does not cut the thread and was ignored.");
    }
    else if (missed > 1)
    {
        reportFeatureWarning(context, id, "Some stop selections do not cut the thread and were ignored.");
    }
    return remaining;
}

function splitSweepKeepThread(context is Context, id is Id, sweep is Query, cutPlane is Plane, localCoordSys is CoordSystem, height is ValueWithUnits) returns Query
{
    if (isQueryEmpty(context, sweep))
    {
        return sweep;
    }
    const keepPoint = localCoordSys.origin + height / 2 * localCoordSys.zAxis;
    opPlane(context, id + "plane", {
                "plane" : cutPlane
            });
    const planeTool = qOwnerBody(qCreatedBy(id + "plane"));
    try
    {
        opSplitPart(context, id + "split", {
                    "targets" : sweep,
                    "tool" : planeTool,
                    "keepTools" : false
                });
    }
    catch
    {
        if (!isQueryEmpty(context, planeTool))
        {
            opDeleteBodies(context, id + "planeDelete", {
                        "entities" : planeTool
                    });
        }
        return sweep;
    }
    const back = qSplitBy(id + "split", EntityType.BODY, true);
    const front = qSplitBy(id + "split", EntityType.BODY, false);
    const sides = pickThreadedSweepSide(context, front, back, localCoordSys, height, keepPoint);
    if (sides.discard is Query && !isQueryEmpty(context, sides.discard))
    {
        opDeleteBodies(context, id + "discard", {
                    "entities" : sides.discard
                });
    }
    if (sides.keep is Query && !isQueryEmpty(context, sides.keep))
    {
        return sides.keep;
    }
    return sweep;
}

function clipThreadSweepToFace(context is Context, id is Id, sweep is Query, localCoordSys is CoordSystem, height is ValueWithUnits) returns Query
{
    var remaining = sweep;
    remaining = splitSweepKeepThread(context, id + "start", remaining, plane(localCoordSys.origin, localCoordSys.zAxis), localCoordSys, height);
    remaining = splitSweepKeepThread(context, id + "end", remaining, plane(localCoordSys.origin + height * localCoordSys.zAxis, localCoordSys.zAxis), localCoordSys, height);
    return remaining;
}

function depthAlongAxis(origin is Vector, zAxis is Vector, point is Vector, errorFields is array) returns ValueWithUnits
{
    const depth = dot(point - origin, zAxis);
    if (depth <= TOLERANCE.zeroLength * meter)
    {
        throw regenError("The end condition is not in the bore direction.", errorFields);
    }
    return depth;
}

function rayHitsAlong(context is Context, entities is Query, origin is Vector, zAxis is Vector) returns array
{
    return evRaycast(context, {
                "entities" : entities,
                "ray" : line(origin, zAxis),
                "closest" : false
            });
}

function firstFarRayDepth(context is Context, entities is Query, origin is Vector, zAxis is Vector, errorFields is array) returns ValueWithUnits
{
    const skip = 0.05 * millimeter;
    for (var hit in rayHitsAlong(context, entities, origin, zAxis))
    {
        if (hit.distance > skip)
        {
            return hit.distance;
        }
    }
    throw regenError("Could not find a face in the bore direction.", errorFields);
}

function lastRayDepth(context is Context, entities is Query, origin is Vector, zAxis is Vector, errorFields is array) returns ValueWithUnits
{
    const hits = rayHitsAlong(context, entities, origin, zAxis);
    if (size(hits) == 0)
    {
        throw regenError("Could not find a solid in the bore direction.", errorFields);
    }
    return max(hits[size(hits) - 1].distance, 0.05 * millimeter);
}

function depthToFace(context is Context, face is Query, origin is Vector, zAxis is Vector) returns ValueWithUnits
{
    const hits = rayHitsAlong(context, face, origin, zAxis);
    if (size(hits) > 0)
    {
        return max(hits[0].distance, 0.05 * millimeter);
    }
    const tangent = evFaceTangentPlane(context, {
                "face" : face,
                "parameter" : vector(0.5, 0.5)
            });
    const denom = dot(tangent.normal, zAxis);
    if (abs(denom) <= TOLERANCE.zeroAngle)
    {
        throw regenError("The end face is parallel to the bore direction.", ["pointEndEntity", "pointOpposite"]);
    }
    return depthAlongAxis(origin, zAxis, origin + zAxis * (dot(tangent.origin - origin, tangent.normal) / denom), ["pointEndEntity", "pointOpposite"]);
}

function computeBoreDepth(context is Context, borePoint is map, located is map, exclude is Query) returns ValueWithUnits
{
    const origin = located.coordSystem.origin;
    const zAxis = located.coordSystem.zAxis;
    const world = qSubtraction(qAllModifiableSolidBodies(), exclude);
    if (borePoint.pointEndType == BoreEndType.BLIND)
    {
        return borePoint.pointHoleDepth;
    }
    else if (borePoint.pointEndType == BoreEndType.THROUGH_ALL)
    {
        return lastRayDepth(context, world, origin, zAxis, ["pointEndType", "pointOpposite"]);
    }
    else if (borePoint.pointEndType == BoreEndType.UP_TO_NEXT)
    {
        return firstFarRayDepth(context, world, origin, zAxis, ["pointEndType", "pointOpposite"]);
    }
    else if (borePoint.pointEndType == BoreEndType.UP_TO_FACE)
    {
        const faces = evaluateQuery(context, qEntityFilter(borePoint.pointEndEntity, EntityType.FACE));
        if (size(faces) != 1)
        {
            throw regenError("Select the face to bore up to.", ["pointEndEntity"]);
        }
        return depthToFace(context, faces[0], origin, zAxis);
    }
    else if (borePoint.pointEndType == BoreEndType.UP_TO_PART)
    {
        var endPart = qUnion(evaluateQuery(context, qBodyType(borePoint.pointEndEntity, BodyType.SOLID)));
        if (isQueryEmpty(context, endPart))
        {
            endPart = qUnion(evaluateQuery(context, qBodyType(qOwnerBody(borePoint.pointEndEntity), BodyType.SOLID)));
        }
        if (isQueryEmpty(context, endPart))
        {
            throw regenError("Select the part to bore up to.", ["pointEndEntity"]);
        }
        return firstFarRayDepth(context, endPart, origin, zAxis, ["pointEndEntity", "pointOpposite"]);
    }
    const vertices = evaluateQuery(context, qEntityFilter(borePoint.pointEndEntity, EntityType.VERTEX));
    if (size(vertices) != 1)
    {
        throw regenError("Select the vertex to bore up to.", ["pointEndEntity"]);
    }
    return depthAlongAxis(origin, zAxis, evVertexPoint(context, {
                        "vertex" : vertices[0]
                    }), ["pointEndEntity", "pointOpposite"]);
}

function defaultBorePointSettings(threadLength is ValueWithUnits, opposite is boolean, label is string) returns map
{
    return {
            "pointLocation" : qNothing(),
            "pointLabel" : label,
            "pointEndType" : BoreEndType.BLIND,
            "pointOpposite" : opposite,
            "pointHoleDepth" : threadLength,
            "pointEndEntity" : qNothing(),
            "pointCounterbore" : true,
            "pointStartChamfer" : false,
            "pointEndChamfer" : false
        };
}

function defaultLocationPointSettings(definition is map, threadLength is ValueWithUnits, opposite is boolean, label is string, isInternal is boolean) returns map
{
    var item = {
            "genLocation" : qNothing(),
            "genLabel" : label,
            "genEndType" : BoreEndType.BLIND,
            "genOpposite" : opposite,
            "genHoleDepth" : threadLength,
            "genEndEntity" : qNothing(),
            "genCounterbore" : true,
            "genStartChamfer" : false,
            "genEndChamfer" : false
        };
    if (isInternal)
    {
        item.genCounterbore = definition.counterbore != false;
        item.genStartChamfer = definition.startChamfer == true;
        item.genCounterboreDeeper = definition.counterboreDeeper;
        item.genCounterboreBack = definition.counterboreBack;
        item.genStartChamferWidth = definition.startChamferWidth;
        item.genStartChamferAngle = definition.startChamferAngle;
    }
    else
    {
        item.genEndChamfer = definition.chamferEnd == true || definition.dieEndChamfer == true;
        item.genEndChamferWidth = definition.chamferWidth is ValueWithUnits ? definition.chamferWidth : definition.dieChamferWidth;
        item.genEndChamferAngle = definition.chamferAngle is ValueWithUnits ? definition.chamferAngle : definition.dieChamferAngle;
    }
    return item;
}

function defaultGeneratedClockSettings(label is string) returns map
{
    return {
            "genClockLocation" : qNothing(),
            "genClockLabel" : label,
            "genClockAngle" : 0 * degree,
            "genClockSense" : ClockSense.CLOCKWISE
        };
}

function generatedPointAsBorePoint(item is map) returns map
{
    return {
            "pointLocation" : item.genLocation,
            "pointLabel" : item.genLabel,
            "pointKey" : item.genKey,
            "pointEndType" : item.genEndType,
            "pointOpposite" : item.genOpposite,
            "pointHoleDepth" : item.genHoleDepth,
            "pointEndEntity" : item.genEndEntity,
            "pointCounterbore" : item.genCounterbore,
            "pointCounterboreDeeper" : item.genCounterboreDeeper,
            "pointCounterboreBack" : item.genCounterboreBack,
            "pointStartChamfer" : item.genStartChamfer,
            "pointStartChamferWidth" : item.genStartChamferWidth,
            "pointStartChamferAngle" : item.genStartChamferAngle,
            "pointEndChamfer" : item.genEndChamfer,
            "pointEndChamferWidth" : item.genEndChamferWidth,
            "pointEndChamferAngle" : item.genEndChamferAngle
        };
}

function generatedClockAsClockPoint(item is map) returns map
{
    return {
            "clockLocation" : item.genClockLocation,
            "clockLabel" : item.genClockLabel,
            "clockKey" : item.genClockKey,
            "clockAngle" : item.genClockAngle,
            "clockSense" : item.genClockSense
        };
}

function generatedPointsAsBorePoints(items is array) returns array
{
    var mapped = [];
    for (var item in items)
    {
        mapped = append(mapped, generatedPointAsBorePoint(item));
    }
    return mapped;
}

function generatedClocksAsClockPoints(items is array) returns array
{
    var mapped = [];
    for (var item in items)
    {
        mapped = append(mapped, generatedClockAsClockPoint(item));
    }
    return mapped;
}

function defaultClockPointSettings(label is string) returns map
{
    return {
            "clockLocation" : qNothing(),
            "clockLabel" : label,
            "clockAngle" : 0 * degree,
            "clockSense" : ClockSense.CLOCKWISE
        };
}

function borePointSettingsAt(pointItems is array, index is number, threadLength is ValueWithUnits, opposite is boolean) returns map
{
    if (index < size(pointItems))
    {
        return pointItems[index];
    }
    return defaultBorePointSettings(threadLength, opposite, "Hole");
}

function clockPointSettingsAt(pointItems is array, index is number) returns map
{
    if (index < size(pointItems))
    {
        return pointItems[index];
    }
    return defaultClockPointSettings("Hole");
}

function signedClockAngle(clockPoint is map) returns ValueWithUnits
{
    const angle = clockPoint.clockAngle is ValueWithUnits ? clockPoint.clockAngle : 0 * degree;
    // Positive rotation around the hole axis is clockwise when looking along the
    // bore, from the start face into the hole.
    if (clockPoint.clockSense == ClockSense.CLOCKWISE)
    {
        return angle;
    }
    return -angle;
}

function clockedCoordSystem(cSys is CoordSystem, clockAngle is ValueWithUnits) returns CoordSystem
{
    if (abs(clockAngle) <= TOLERANCE.zeroAngle * radian)
    {
        return cSys;
    }
    return coordSystem(cSys.origin, rotationMatrix3d(cSys.zAxis, clockAngle) * cSys.xAxis, cSys.zAxis);
}

function halfTurnCoordSystem(cSys is CoordSystem) returns CoordSystem
{
    return coordSystem(cSys.origin, -cSys.xAxis, cSys.zAxis);
}

function threadToolCoordSystem(cSys is CoordSystem, spec is map) returns CoordSystem
{
    return spec.alignHalfTurn == true ? halfTurnCoordSystem(cSys) : cSys;
}

function helixXAtAxial(generatorCSys is CoordSystem, pitch is ValueWithUnits, leftHanded is boolean, along is ValueWithUnits) returns Vector
{
    const turn = (along / pitch) * 2 * PI * radian;
    // opHelix clockwise = !leftHanded: looking along +Z, the start point turns clockwise as Z increases.
    return rotationMatrix3d(generatorCSys.zAxis, leftHanded ? turn : -turn) * generatorCSys.xAxis;
}

function alignGeneratedPlacement(spec is map, locatedCSys is CoordSystem) returns map
{
    if (!(spec.generatorCSys is CoordSystem) || !(spec.pitch is ValueWithUnits))
    {
        return {
                "coordSystem" : locatedCSys,
                "leftHanded" : spec.leftHanded == true
            };
    }
    const generatorCSys = spec.generatorCSys;
    const along = dot(locatedCSys.origin - generatorCSys.origin, generatorCSys.zAxis);
    var x = helixXAtAxial(generatorCSys, spec.pitch, spec.leftHanded == true, along);
    const z = locatedCSys.zAxis;
    x = x - dot(x, z) * z;
    if (squaredNorm(x) <= TOLERANCE.zeroLength * TOLERANCE.zeroLength)
    {
        x = perpendicularVector(z);
    }
    else
    {
        x = normalize(x);
    }
    if (spec.alignHalfTurn == true)
    {
        x = -x;
    }
    const flipped = dot(z, generatorCSys.zAxis) < 0;
    return {
            "coordSystem" : coordSystem(locatedCSys.origin, x, z),
            "leftHanded" : flipped ? spec.leftHanded != true : spec.leftHanded == true
        };
}

function oppositeEndThreadFrame(generatorCSys is CoordSystem, length is ValueWithUnits, pitch is ValueWithUnits, leftHanded is boolean, alignHalfTurn is boolean) returns map
{
    const origin = generatorCSys.origin + length * generatorCSys.zAxis;
    const z = -generatorCSys.zAxis;
    var x = helixXAtAxial(generatorCSys, pitch, leftHanded, length);
    x = x - dot(x, z) * z;
    if (squaredNorm(x) <= TOLERANCE.zeroLength * TOLERANCE.zeroLength)
    {
        x = perpendicularVector(z);
    }
    else
    {
        x = normalize(x);
    }
    if (alignHalfTurn)
    {
        x = -x;
    }
    return {
            "coordSystem" : coordSystem(origin, x, z),
            "leftHanded" : !leftHanded
        };
}

function threadLengthFromDefinition(context is Context, definition is map) returns ValueWithUnits
{
    if (!(definition.surface is Query) || isQueryEmpty(context, definition.surface))
    {
        return 50 * millimeter;
    }
    const frame = try silent(threadSurfaceFrame(context, definition.surface, false));
    if (frame is map)
    {
        return frame.height;
    }
    return 50 * millimeter;
}

function locationKey(context is Context, location is Query) returns string
{
    const axis = try silent(evAxis(context, {
                    "axis" : location,
                    "allowSketchPoints" : true
                }));
    if (axis is Line)
    {
        return "" ~ round(axis.origin[0] / meter * 1e8) ~ "," ~ round(axis.origin[1] / meter * 1e8) ~ "," ~ round(axis.origin[2] / meter * 1e8) ~ "@" ~ round(axis.direction[0] * 1e6) ~ "," ~ round(axis.direction[1] * 1e6) ~ "," ~ round(axis.direction[2] * 1e6);
    }
    return "unknown";
}

function holeLabel(index is number) returns string
{
    return "Hole " ~ (index + 1);
}

function threadDisplayName(definition is map) returns string
{
    if (definition.name is string && definition.name != "")
    {
        return definition.name;
    }
    return "Printable Thread";
}

function prefixedPartName(definition is map, suffix is string) returns string
{
    return threadDisplayName(definition) ~ " " ~ suffix;
}

function locationRoleLabel(known is boolean, primaryIsInternal is boolean, generated is boolean) returns string
{
    if (known != true)
    {
        return generated ? "Initial" : "Mating";
    }
    if (generated)
    {
        return primaryIsInternal ? "Internal" : "External";
    }
    return primaryIsInternal ? "External" : "Internal";
}

function queryLocations(context is Context, locations) returns array
{
    if (locations is Query)
    {
        return evaluateQuery(context, locations);
    }
    return [];
}

function pointBoolean(point is map, field is string, fallback is boolean) returns boolean
{
    if (point[field] is boolean)
    {
        return point[field];
    }
    return fallback;
}

function pointLength(point is map, field is string, fallback)
{
    if (point[field] is ValueWithUnits)
    {
        return point[field];
    }
    return fallback;
}

function joinLabels(labels is array) returns string
{
    var text = "";
    for (var i = 0; i < size(labels); i += 1)
    {
        if (i > 0)
        {
            text ~= ", ";
        }
        text ~= labels[i];
    }
    return text;
}

function deleteCreatedSilent(context is Context, id is Id)
{
    try silent
    {
        const created = qCreatedBy(id);
        if (!isQueryEmpty(context, created))
        {
            opDeleteBodies(context, id + "deleteFailed", {
                        "entities" : created
                    });
        }
    }
}

function wantsInternalCounterbore(definition is map) returns boolean
{
    return definition.counterbore == true || definition.holeCounterbore == true;
}

function wantsInternalStartChamfer(definition is map) returns boolean
{
    return definition.startChamfer == true || definition.holeStartChamfer == true;
}

function wantsExternalEndChamfer(definition is map) returns boolean
{
    return definition.chamferEnd == true || definition.dieEndChamfer == true;
}

function internalCounterboreDeeper(definition is map)
{
    if (definition.counterboreDeeper is ValueWithUnits)
    {
        return definition.counterboreDeeper;
    }
    return definition.holeCounterboreDeeper is ValueWithUnits ? definition.holeCounterboreDeeper : 1 * millimeter;
}

function internalCounterboreBack(definition is map)
{
    if (definition.counterboreBack is ValueWithUnits)
    {
        return definition.counterboreBack;
    }
    return definition.holeCounterboreBack is ValueWithUnits ? definition.holeCounterboreBack : 2 * millimeter;
}

function internalStartChamferWidth(definition is map)
{
    if (definition.startChamferWidth is ValueWithUnits)
    {
        return definition.startChamferWidth;
    }
    return definition.holeStartChamferWidth is ValueWithUnits ? definition.holeStartChamferWidth : 2 * millimeter;
}

function internalStartChamferAngle(definition is map)
{
    if (definition.startChamferAngle is ValueWithUnits)
    {
        return definition.startChamferAngle;
    }
    return definition.holeStartChamferAngle is ValueWithUnits ? definition.holeStartChamferAngle : 45 * degree;
}

function externalChamferWidth(definition is map)
{
    if (definition.chamferWidth is ValueWithUnits)
    {
        return definition.chamferWidth;
    }
    return definition.dieChamferWidth is ValueWithUnits ? definition.dieChamferWidth : 2 * millimeter;
}

function externalChamferAngle(definition is map)
{
    if (definition.chamferAngle is ValueWithUnits)
    {
        return definition.chamferAngle;
    }
    return definition.dieChamferAngle is ValueWithUnits ? definition.dieChamferAngle : 45 * degree;
}

function drivenItemsNeedSync(context is Context, specifiedDriving is boolean, specifiedLocations is boolean, drivingJustEnabled is boolean, oldItems, items is array, locations is array, locationField is string) returns boolean
{
    if (specifiedDriving || specifiedLocations)
    {
        return true;
    }
    if (oldItems is array && size(oldItems) > size(items))
    {
        return true;
    }
    if (size(items) != size(locations))
    {
        return true;
    }
    if (drivingJustEnabled)
    {
        return true;
    }
    for (var item in items)
    {
        if (!(item[locationField] is Query) || isQueryEmpty(context, item[locationField]))
        {
            return true;
        }
    }
    return false;
}

function rematchHoleItems(context is Context, locations is array, items is array, defaultItem is map, locationField is string, keyField is string, labelField is string) returns array
{
    var unused = [];
    for (var item in items)
    {
        unused = append(unused, item);
    }
    var next = [];
    for (var i = 0; i < size(locations); i += 1)
    {
        const key = locationKey(context, locations[i]);
        var matched = {};
        var hasMatch = false;
        var remaining = [];
        for (var item in unused)
        {
            if (!hasMatch && item[keyField] == key)
            {
                matched = item;
                hasMatch = true;
            }
            else
            {
                remaining = append(remaining, item);
            }
        }
        unused = remaining;
        if (!hasMatch || !(matched[locationField] is Query) || isQueryEmpty(context, matched[locationField]))
        {
            matched = defaultItem;
        }
        matched[locationField] = locations[i];
        matched[keyField] = key;
        matched[labelField] = holeLabel(i);
        next = append(next, matched);
    }
    return next;
}

function syncBorePointSettings(context is Context, oldDefinition is map, definition is map, specifiedParameters is map) returns map
{
    if (definition.specifyBoreEnd != true || !(definition.boreLocations is Query))
    {
        return definition;
    }
    const locations = evaluateQuery(context, definition.boreLocations);
    var items = definition.borePoints is array ? definition.borePoints : [];
    if (!drivenItemsNeedSync(context, specifiedParameters.specifyBoreEnd == true, specifiedParameters.boreLocations == true, oldDefinition.specifyBoreEnd != true && definition.specifyBoreEnd == true, oldDefinition.borePoints, items, locations, "pointLocation"))
    {
        return definition;
    }
    definition.borePoints = rematchHoleItems(context, locations, items, defaultBorePointSettings(threadLengthFromDefinition(context, definition), definition.boreOppositeDirection == true, "Hole"), "pointLocation", "pointKey", "pointLabel");
    return definition;
}

function syncClockPointSettings(context is Context, oldDefinition is map, definition is map, specifiedParameters is map) returns map
{
    if (definition.clockBores != true || !(definition.boreLocations is Query))
    {
        return definition;
    }
    const locations = evaluateQuery(context, definition.boreLocations);
    var items = definition.clockPoints is array ? definition.clockPoints : [];
    if (!drivenItemsNeedSync(context, specifiedParameters.clockBores == true || specifiedParameters.clockPoints == true, specifiedParameters.boreLocations == true, oldDefinition.clockBores != true && definition.clockBores == true, oldDefinition.clockPoints, items, locations, "clockLocation"))
    {
        return definition;
    }
    definition.clockPoints = rematchHoleItems(context, locations, items, defaultClockPointSettings("Hole"), "clockLocation", "clockKey", "clockLabel");
    return definition;
}

function syncGeneratedPointSettings(context is Context, oldDefinition is map, definition is map, specifiedParameters is map) returns map
{
    if (definition.specifyGeneratedEnd != true || !(definition.generatedLocations is Query))
    {
        return definition;
    }
    const locations = evaluateQuery(context, definition.generatedLocations);
    var items = definition.generatedPoints is array ? definition.generatedPoints : [];
    if (!drivenItemsNeedSync(context, specifiedParameters.specifyGeneratedEnd == true, specifiedParameters.generatedLocations == true, oldDefinition.specifyGeneratedEnd != true && definition.specifyGeneratedEnd == true, oldDefinition.generatedPoints, items, locations, "genLocation"))
    {
        return definition;
    }
    const isInternal = definition.primaryIsInternal == true;
    definition.generatedPoints = rematchHoleItems(context, locations, items, defaultLocationPointSettings(definition, threadLengthFromDefinition(context, definition), definition.generatedOpposite == true, "Location", isInternal), "genLocation", "genKey", "genLabel");
    return definition;
}

function syncGeneratedClockSettings(context is Context, oldDefinition is map, definition is map, specifiedParameters is map) returns map
{
    if (definition.clockGenerated != true || !(definition.generatedLocations is Query))
    {
        return definition;
    }
    const locations = evaluateQuery(context, definition.generatedLocations);
    var items = definition.generatedClockPoints is array ? definition.generatedClockPoints : [];
    if (!drivenItemsNeedSync(context, specifiedParameters.clockGenerated == true || specifiedParameters.generatedClockPoints == true, specifiedParameters.generatedLocations == true, oldDefinition.clockGenerated != true && definition.clockGenerated == true, oldDefinition.generatedClockPoints, items, locations, "genClockLocation"))
    {
        return definition;
    }
    definition.generatedClockPoints = rematchHoleItems(context, locations, items, defaultGeneratedClockSettings("Location"), "genClockLocation", "genClockKey", "genClockLabel");
    return definition;
}

function solidsExcept(context is Context, solids is Query, exclude is Query) returns Query
{
    if (!(solids is Query) || isQueryEmpty(context, solids))
    {
        return qNothing();
    }
    if (!(exclude is Query) || isQueryEmpty(context, exclude))
    {
        return solids;
    }
    var kept = [];
    for (var body in evaluateQuery(context, qBodyType(solids, BodyType.SOLID)))
    {
        if (isQueryEmpty(context, qIntersection([body, exclude])))
        {
            kept = append(kept, body);
        }
    }
    if (size(kept) == 0)
    {
        return qNothing();
    }
    return qUnion(kept);
}

function resolveBoreOwner(context is Context, candidate is Query, location is Query, origin is Vector, exclude is Query) returns Query
{
    const allowed = solidsExcept(context, qAllModifiableSolidBodies(), exclude);
    var owner = solidsExcept(context, qUnion(evaluateQuery(context, qBodyType(candidate, BodyType.SOLID))), exclude);
    if (!isQueryEmpty(context, owner))
    {
        return owner;
    }
    owner = solidsExcept(context, qUnion(evaluateQuery(context, qBodyType(qOwnerBody(location), BodyType.SOLID))), exclude);
    if (!isQueryEmpty(context, owner))
    {
        return owner;
    }
    for (var solid in evaluateQuery(context, allowed))
    {
        if (!isQueryEmpty(context, qIntersection([location, qMateConnectorsOfParts(solid)])))
        {
            return solid;
        }
    }
    owner = qClosestTo(allowed, origin);
    if (isQueryEmpty(context, owner))
    {
        return qNothing();
    }
    return qUnion(evaluateQuery(context, owner));
}

function borePointLocation(context is Context, location is Query, oppositeDirection is boolean, exclude is Query) returns map
{
    var axis = evAxis(context, {
                "axis" : location,
                "allowSketchPoints" : true
            });
    if (!oppositeDirection)
    {
        axis.direction *= -1;
    }
    const xAxis = perpendicularVector(axis.direction);
    return {
            "coordSystem" : coordSystem(axis.origin, xAxis, axis.direction),
            "owner" : resolveBoreOwner(context, qNothing(), location, axis.origin, exclude)
        };
}

function placementTargets(context is Context, tool is Query, owner is Query, exclude is Query) returns Query
{
    const ignore = qUnion([exclude, tool, qOwnerBody(tool)]);
    var candidates = solidsExcept(context, qAllModifiableSolidBodies(), ignore);
    if (!isQueryEmpty(context, owner))
    {
        candidates = qUnion([candidates, solidsExcept(context, owner, ignore)]);
    }
    return solidsExcept(context, collidingSolids(context, tool, candidates), ignore);
}

function startChamferDimensions(radius is ValueWithUnits, width is ValueWithUnits, angle is ValueWithUnits, tapLength is ValueWithUnits) returns map
{
    const slope = tan(angle);
    const outerRadius = radius + width / slope;
    const overlap = 0.05 * millimeter;
    const zApex = outerRadius * slope;
    const zEnd = min(zApex, tapLength);
    const rBack = outerRadius + overlap / slope;
    const rAtEnd = zEnd >= zApex - TOLERANCE.zeroLength * meter ? 0 * meter : rBack * (zApex - zEnd) / (zApex + overlap);
    return {
            "outerRadius" : outerRadius,
            "zApex" : zApex,
            "zEnd" : zEnd,
            "rAtEnd" : rAtEnd,
            "overlap" : overlap,
            "rBack" : rBack
        };
}

function createStartChamfer(context is Context, id is Id, localCoordSys is CoordSystem, radius is ValueWithUnits, width is ValueWithUnits, angle is ValueWithUnits, tapLength is ValueWithUnits) returns Query
{
    const dims = startChamferDimensions(radius, width, angle, tapLength);
    if (dims.rAtEnd > TOLERANCE.zeroLength * meter)
    {
        return revolveAxisProfile(context, id, localCoordSys, [
                    vector(dims.rBack, -dims.overlap),
                    vector(dims.rAtEnd, dims.zEnd),
                    vector(0 * meter, dims.zEnd),
                    vector(0 * meter, -dims.overlap),
                    vector(dims.rBack, -dims.overlap)
                ]);
    }
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(dims.rBack, -dims.overlap),
                vector(0 * meter, dims.zEnd),
                vector(0 * meter, -dims.overlap),
                vector(dims.rBack, -dims.overlap)
            ]);
}

function createCounterboreWithCone(context is Context, id is Id, localCoordSys is CoordSystem, zTop is ValueWithUnits, zBottom is ValueWithUnits, radius is ValueWithUnits, coneHeight is ValueWithUnits) returns Query
{
    const zLimit = 0 * meter;
    const zApex = zTop - coneHeight;
    const rInner = max(radius - coneHeight, 0 * meter);
    if (zApex >= zLimit - TOLERANCE.zeroLength * meter)
    {
        return revolveAxisProfile(context, id, localCoordSys, [
                    vector(radius, zBottom),
                    vector(radius, zTop),
                    vector(rInner, zApex),
                    vector(0 * meter, zApex),
                    vector(0 * meter, zBottom),
                    vector(radius, zBottom)
                ]);
    }
    if (zTop <= zLimit + TOLERANCE.zeroLength * meter)
    {
        return revolveAxisProfile(context, id, localCoordSys, [
                    vector(radius, zBottom),
                    vector(radius, zTop),
                    vector(0 * meter, zTop),
                    vector(0 * meter, zBottom),
                    vector(radius, zBottom)
                ]);
    }
    const rAtLimit = max(radius + ((zTop - zLimit) / coneHeight) * (rInner - radius), 0 * meter);
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(radius, zBottom),
                vector(radius, zTop),
                vector(rAtLimit, zLimit),
                vector(0 * meter, zLimit),
                vector(0 * meter, zBottom),
                vector(radius, zBottom)
            ]);
}

function fittedCounterboreBack(back is ValueWithUnits, tapLength is ValueWithUnits, strict is boolean) returns ValueWithUnits
{
    if (back < tapLength)
    {
        return back;
    }
    if (strict)
    {
        throw regenError("Counterbore back is longer than the tap.", ["counterboreBack"]);
    }
    return max(tapLength - 0.05 * millimeter, 0 * meter);
}

function holeUsesCounterbore(specifyEnd is boolean, borePoint is map) returns boolean
{
    return specifyEnd != true || borePoint.pointEndType != BoreEndType.THROUGH_ALL;
}

function threadLockFaces(bodyOrFace is Query) returns Query
{
    return qUnion([
            qGeometry(bodyOrFace, GeometryType.CYLINDER),
            qGeometry(bodyOrFace, GeometryType.CONE)
        ]);
}

function tapMatchesInternal(spec is map) returns boolean
{
    return spec.cutsInward == false;
}

function tapHelixRadiusAt(spec is map, axial is ValueWithUnits) returns ValueWithUnits
{
    return threadRadiusAt(spec.startRadius, spec.endRadius, spec.height, axial, spec.threadClearance);
}

function tapMajorRadiusAt(spec is map, axial is ValueWithUnits) returns ValueWithUnits
{
    const helix = tapHelixRadiusAt(spec, axial);
    if (tapMatchesInternal(spec))
    {
        return helix + spec.depth;
    }
    return helix;
}

function createThreadedTap(context is Context, id is Id, spec is map) returns Query
{
    const localCoordSys = spec.localCoordSys;
    const tapLength = spec.tapLength;
    const tapRevs = tapLength / spec.pitch;
    const tapEndRadius = tapHelixRadiusAt(spec, tapLength);
    var tap = createTapStock(context, id + "stock", localCoordSys, spec.startRadius, spec.endRadius, spec.height, tapLength, spec.threadClearance);
    const lockFaces = threadLockFaces(qOwnedByBody(tap, EntityType.FACE));
    opHelix(context, id + "helix", {
                "direction" : localCoordSys.zAxis,
                "axisStart" : localCoordSys.origin,
                "startPoint" : toWorld(localCoordSys, vector(spec.startRadius + spec.threadClearance, 0 * meter, 0 * meter)),
                "interval" : [-spec.extraRevs, tapRevs + spec.extraRevs],
                "clockwise" : !spec.leftHanded,
                "helicalPitch" : spec.pitch,
                "spiralPitch" : tapRevs == 0 ? 0 * meter : (tapEndRadius - (spec.startRadius + spec.threadClearance)) / tapRevs
            });
    const tapHelixEdge = qCreatedBy(id + "helix", EntityType.EDGE);
    const tapStartTangent = evEdgeTangentLine(context, {
                "edge" : tapHelixEdge,
                "parameter" : 0,
                "arcLengthParameterization" : false
            });
    const tapIntoMaterial = intoMaterialDirection(localCoordSys, tapStartTangent.origin, spec.cutsInward);
    const tapProfile = newSketchOnPlane(context, id + "profile", {
                "sketchPlane" : threadProfilePlane(tapStartTangent.origin, tapIntoMaterial, localCoordSys.zAxis)
            });
    skPolyline(tapProfile, "profile", {
                "points" : threadProfilePoints(spec.overlap, spec.depth, spec.outerHalfWidth, spec.rootHalfWidth, spec.truncation)
            });
    skSolve(tapProfile);

    try
    {
        var tapSweep = {
                    "profiles" : qSketchRegion(id + "profile"),
                    "path" : tapHelixEdge
                };
        if (!isQueryEmpty(context, lockFaces))
        {
            tapSweep.lockFaces = lockFaces;
        }
        opSweep(context, id + "sweep", tapSweep);
    }
    catch
    {
        opDeleteBodies(context, id + "deleteFailedConstruction", {
                    "entities" : qUnion([
                            qCreatedBy(id + "helix", EntityType.BODY),
                            qCreatedBy(id + "profile", EntityType.BODY)
                        ])
                });
        throw regenError("Could not sweep the tap thread profile.");
    }
    opDeleteBodies(context, id + "deleteConstruction", {
                "entities" : qUnion([
                        qCreatedBy(id + "helix", EntityType.BODY),
                        qCreatedBy(id + "profile", EntityType.BODY)
                    ])
            });

    var sweepBody = qCreatedBy(id + "sweep", EntityType.BODY);
    if (tapMatchesInternal(spec))
    {
        sweepBody = clipThreadSweepToFace(context, id + "trim", sweepBody, localCoordSys, tapLength);
    }

    try
    {
        if (tapMatchesInternal(spec))
        {
            opBoolean(context, id + "cut", {
                        "tools" : qUnion([tap, sweepBody]),
                        "operationType" : BooleanOperationType.UNION
                    });
        }
        else
        {
            opBoolean(context, id + "cut", {
                        "tools" : sweepBody,
                        "targets" : tap,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
    }
    catch
    {
        throw regenError("Could not build the tap.");
    }
    const joinedTap = qCreatedBy(id + "cut", EntityType.BODY);
    if (!isQueryEmpty(context, joinedTap))
    {
        tap = joinedTap;
    }

    if (spec.useCounterbore != false)
    {
        const counterboreBack = fittedCounterboreBack(spec.counterboreBack, tapLength, spec.strictCounterbore == true);
        const boreOverlap = max(counterboreBack, 0.05 * millimeter);
        const cutoutEnd = tapLength + spec.counterboreDeeper;
        if (cutoutEnd > tapLength - boreOverlap + TOLERANCE.zeroLength * meter)
        {
            const cutout = createCounterboreWithCone(context, id + "counterbore", localCoordSys, tapLength - boreOverlap, cutoutEnd, tapMajorRadiusAt(spec, tapLength), spec.depth);
            try
            {
                opBoolean(context, id + "bores", {
                            "tools" : qUnion([tap, cutout]),
                            "operationType" : BooleanOperationType.UNION
                        });
            }
            catch
            {
                throw regenError("Could not add the tap counterbore cylinders.");
            }
            const unionedTap = qCreatedBy(id + "bores", EntityType.BODY);
            if (!isQueryEmpty(context, unionedTap))
            {
                tap = unionedTap;
            }
        }
    }

    if (spec.startChamfer == true)
    {
        const chamferWidth = spec.startChamferWidth;
        if (chamferWidth >= tapLength)
        {
            throw regenError("Start chamfer is larger than the hole depth.", ["startChamferWidth"]);
        }
        const entryRadius = tapHelixRadiusAt(spec, 0 * meter);
        const chamfer = createStartChamfer(context, id + "startChamfer", localCoordSys, entryRadius, chamferWidth, spec.startChamferAngle, tapLength);
        try
        {
            opBoolean(context, id + "startChamferUnion", {
                        "tools" : qUnion([tap, chamfer]),
                        "operationType" : BooleanOperationType.UNION
                    });
        }
        catch
        {
            throw regenError("Could not add the hole start chamfer.", ["startChamferWidth", "startChamferAngle"]);
        }
        const chamferedTap = qCreatedBy(id + "startChamferUnion", EntityType.BODY);
        if (!isQueryEmpty(context, chamferedTap))
        {
            tap = chamferedTap;
        }
    }

    if (spec.partName is string)
    {
        setProperty(context, {
                    "entities" : tap,
                    "propertyType" : PropertyType.NAME,
                    "value" : spec.partName
                });
    }
    if (spec.addMate == true)
    {
        addTapMateConnector(context, id + "mate", localCoordSys.origin, localCoordSys.xAxis, localCoordSys.zAxis, tap);
    }
    return tap;
}

function addTapMateConnector(context is Context, id is Id, origin is Vector, xAxis is Vector, zAxis is Vector, owner is Query)
{
    opMateConnector(context, id, {
                "coordSystem" : coordSystem(origin, xAxis, zAxis),
                "owner" : owner,
                "attachTo" : owner
            });
}

function setTapPartName(context is Context, entities is Query, name is string)
{
    setProperty(context, {
                "entities" : entities,
                "propertyType" : PropertyType.NAME,
                "value" : name
            });
}

function keptTapWantsThrough(form) returns boolean
{
    return form == KeptTapForm.WITHOUT_COUNTERBORE || form == KeptTapForm.BOTH;
}

function keptTapWantsThread(form) returns boolean
{
    return form == KeptTapForm.WITH_COUNTERBORE || form == KeptTapForm.BOTH;
}

function throughTapChamferWidth(definition is map) returns ValueWithUnits
{
    return definition.startChamferWidth is ValueWithUnits ? definition.startChamferWidth : 2 * millimeter;
}

function throughTapChamferAngle(definition is map) returns ValueWithUnits
{
    return definition.startChamferAngle is ValueWithUnits ? definition.startChamferAngle : 45 * degree;
}

function createThroughTapParts(context is Context, id is Id, spec is map)
{
    const localCoordSys = spec.localCoordSys;
    const tapLength = spec.tapLength;
    var threadSpec = spec;
    threadSpec.useCounterbore = false;
    threadSpec.startChamfer = false;
    threadSpec.addMate = false;
    threadSpec.partName = spec.namePrefix is string ? spec.namePrefix ~ " Internal Through Tap" : "Internal Through Tap";
    const thread = createThreadedTap(context, id + "thread", threadSpec);
    addTapMateConnector(context, id + "threadStart", localCoordSys.origin, localCoordSys.xAxis, localCoordSys.zAxis, thread);
    addTapMateConnector(context, id + "threadEnd", localCoordSys.origin + tapLength * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, thread);

    if (spec.startChamfer == true)
    {
        const entryRadius = tapHelixRadiusAt(spec, 0 * meter);
        const chamferDims = startChamferDimensions(entryRadius, spec.startChamferWidth, spec.startChamferAngle, tapLength);
        const chamfer = createStartChamfer(context, id + "chamfer", localCoordSys, entryRadius, spec.startChamferWidth, spec.startChamferAngle, tapLength);
        setTapPartName(context, chamfer, spec.namePrefix is string ? spec.namePrefix ~ " Internal Through Tap Chamfer" : "Internal Through Tap Chamfer");
        addTapMateConnector(context, id + "chamferTop", localCoordSys.origin - chamferDims.overlap * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, chamfer);
    }

    if (spec.useCounterbore == true)
    {
        const tapEndRadius = tapMajorRadiusAt(spec, tapLength);
        const counterboreBack = fittedCounterboreBack(spec.counterboreBack, tapLength, false);
        const boreOverlap = max(counterboreBack, 0.05 * millimeter);
        const cutoutEnd = tapLength + spec.counterboreDeeper;
        const counterbore = createCounterboreWithCone(context, id + "counterbore", localCoordSys, tapLength - boreOverlap, cutoutEnd, tapEndRadius, spec.depth);
        setTapPartName(context, counterbore, spec.namePrefix is string ? spec.namePrefix ~ " Internal Through Tap Counterbore" : "Internal Through Tap Counterbore");
        addTapMateConnector(context, id + "counterboreMeet", localCoordSys.origin + tapLength * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, counterbore);
    }
}

function complementaryThreadRadiusAt(startRadius is ValueWithUnits, endRadius is ValueWithUnits, height is ValueWithUnits, axial is ValueWithUnits, clearance is ValueWithUnits) returns ValueWithUnits
{
    const radius = startRadius + (endRadius - startRadius) * (axial / height) - clearance;
    if (radius <= 0.05 * millimeter)
    {
        throw regenError("Thread clearance is too large for the hole radius.", ["threadClearance"]);
    }
    return radius;
}

function matingCrestRadiusAt(startRadius is ValueWithUnits, endRadius is ValueWithUnits, height is ValueWithUnits, axial is ValueWithUnits, clearance is ValueWithUnits, depth is ValueWithUnits) returns ValueWithUnits
{
    return complementaryThreadRadiusAt(startRadius, endRadius, height, axial, clearance) + depth;
}

function dieMatchesExternal(spec is map) returns boolean
{
    return spec.matchExternal == true;
}

function dieHelixRadiusAt(spec is map, axial is ValueWithUnits) returns ValueWithUnits
{
    if (dieMatchesExternal(spec))
    {
        return threadRadiusAt(spec.startRadius, spec.endRadius, spec.height, axial, spec.threadClearance);
    }
    return complementaryThreadRadiusAt(spec.startRadius, spec.endRadius, spec.height, axial, spec.threadClearance);
}

function dieBoreRadiusAt(spec is map, axial is ValueWithUnits) returns ValueWithUnits
{
    if (dieMatchesExternal(spec))
    {
        return dieHelixRadiusAt(spec, axial);
    }
    return complementaryThreadRadiusAt(spec.startRadius, spec.endRadius, spec.height, axial, spec.threadClearance);
}

function dieCrestRadiusAt(spec is map, axial is ValueWithUnits) returns ValueWithUnits
{
    if (dieMatchesExternal(spec))
    {
        return dieHelixRadiusAt(spec, axial);
    }
    return matingCrestRadiusAt(spec.startRadius, spec.endRadius, spec.height, axial, spec.threadClearance, spec.depth);
}

function createDieStock(context is Context, id is Id, localCoordSys is CoordSystem, spec is map, dieLength is ValueWithUnits, outerRadius is ValueWithUnits) returns Query
{
    const r0 = dieBoreRadiusAt(spec, 0 * meter);
    const r1 = dieBoreRadiusAt(spec, dieLength);
    const crest0 = dieCrestRadiusAt(spec, 0 * meter);
    const crest1 = dieCrestRadiusAt(spec, dieLength);
    const outer = max(outerRadius, max(crest0, crest1) + 0.5 * millimeter);
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(r0, 0 * meter),
                vector(r1, dieLength),
                vector(outer, dieLength),
                vector(outer, 0 * meter),
                vector(r0, 0 * meter)
            ]);
}

function resolvedDieOuterRadius(spec is map) returns ValueWithUnits
{
    const crest0 = dieCrestRadiusAt(spec, 0 * meter);
    const axial = spec.dieLength is ValueWithUnits ? spec.dieLength : spec.height;
    const crest1 = dieCrestRadiusAt(spec, axial);
    const envelope = max(crest0, crest1) + 0.5 * millimeter;
    if (spec.fitThreadOnly == true)
    {
        return envelope;
    }
    const requested = spec.outerRadius is ValueWithUnits ? spec.outerRadius : envelope;
    return max(requested, envelope);
}

function createDieEndChamfer(context is Context, id is Id, localCoordSys is CoordSystem, innerRadius is ValueWithUnits, width is ValueWithUnits, angle is ValueWithUnits) returns Query
{
    const radial = width * tan(angle);
    if (radial >= innerRadius)
    {
        throw regenError("End chamfer is larger than the die bore.", ["dieChamferWidth", "dieChamferAngle"]);
    }
    const overlap = 0.05 * millimeter;
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(innerRadius + overlap, -overlap),
                vector(innerRadius + overlap, width),
                vector(innerRadius, width),
                vector(innerRadius - radial, 0 * meter),
                vector(innerRadius - radial, -overlap),
                vector(innerRadius + overlap, -overlap)
            ]);
}

function unionDieChamfer(context is Context, id is Id, die is Query, chamfer is Query) returns Query
{
    try
    {
        opBoolean(context, id, {
                    "tools" : qUnion([die, chamfer]),
                    "operationType" : BooleanOperationType.UNION
                });
    }
    catch
    {
        throw regenError("Could not add the die end chamfer.", ["dieChamferWidth", "dieChamferAngle"]);
    }
    const unioned = qCreatedBy(id, EntityType.BODY);
    if (!isQueryEmpty(context, unioned))
    {
        return unioned;
    }
    return die;
}

function createThreadedDie(context is Context, id is Id, spec is map) returns Query
{
    const localCoordSys = spec.localCoordSys;
    const dieLength = spec.dieLength;
    const dieRevs = dieLength / spec.pitch;
    const innerStart = dieHelixRadiusAt(spec, 0 * meter);
    const innerEnd = dieHelixRadiusAt(spec, dieLength);
    const crestStart = dieCrestRadiusAt(spec, 0 * meter);
    var die = createDieStock(context, id + "stock", localCoordSys, spec, dieLength, resolvedDieOuterRadius(spec));
    const lockFaces = threadLockFaces(qOwnedByBody(die, EntityType.FACE));
    opHelix(context, id + "helix", {
                "direction" : localCoordSys.zAxis,
                "axisStart" : localCoordSys.origin,
                "startPoint" : toWorld(localCoordSys, vector(innerStart, 0 * meter, 0 * meter)),
                "interval" : [-spec.extraRevs, dieRevs + spec.extraRevs],
                "clockwise" : !spec.leftHanded,
                "helicalPitch" : spec.pitch,
                "spiralPitch" : dieRevs == 0 ? 0 * meter : (innerEnd - innerStart) / dieRevs
            });
    const dieHelixEdge = qCreatedBy(id + "helix", EntityType.EDGE);
    const dieStartTangent = evEdgeTangentLine(context, {
                "edge" : dieHelixEdge,
                "parameter" : 0,
                "arcLengthParameterization" : false
            });
    const dieIntoMaterial = intoMaterialDirection(localCoordSys, dieStartTangent.origin, dieMatchesExternal(spec));
    const dieProfile = newSketchOnPlane(context, id + "profile", {
                "sketchPlane" : threadProfilePlane(dieStartTangent.origin, dieIntoMaterial, localCoordSys.zAxis)
            });
    skPolyline(dieProfile, "profile", {
                "points" : threadProfilePoints(spec.overlap, spec.depth, spec.outerHalfWidth, spec.rootHalfWidth, spec.truncation)
            });
    skSolve(dieProfile);

    try
    {
        var dieSweep = {
                    "profiles" : qSketchRegion(id + "profile"),
                    "path" : dieHelixEdge
                };
        if (!isQueryEmpty(context, lockFaces))
        {
            dieSweep.lockFaces = lockFaces;
        }
        opSweep(context, id + "sweep", dieSweep);
    }
    catch
    {
        opDeleteBodies(context, id + "deleteFailedConstruction", {
                    "entities" : qUnion([
                            qCreatedBy(id + "helix", EntityType.BODY),
                            qCreatedBy(id + "profile", EntityType.BODY)
                        ])
                });
        throw regenError("Could not sweep the die thread profile.");
    }
    opDeleteBodies(context, id + "deleteConstruction", {
                "entities" : qUnion([
                        qCreatedBy(id + "helix", EntityType.BODY),
                        qCreatedBy(id + "profile", EntityType.BODY)
                    ])
            });

    var sweepBody = qCreatedBy(id + "sweep", EntityType.BODY);
    if (dieMatchesExternal(spec))
    {
        sweepBody = clipThreadSweepToFace(context, id + "trim", sweepBody, localCoordSys, dieLength);
    }

    try
    {
        if (dieMatchesExternal(spec))
        {
            opBoolean(context, id + "cut", {
                        "tools" : qUnion([die, sweepBody]),
                        "operationType" : BooleanOperationType.UNION
                    });
        }
        else
        {
            opBoolean(context, id + "cut", {
                        "tools" : sweepBody,
                        "targets" : die,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
    }
    catch
    {
        throw regenError("Could not build the die.");
    }
    const joined = qCreatedBy(id + "cut", EntityType.BODY);
    if (!isQueryEmpty(context, joined))
    {
        die = joined;
    }

    if (spec.endChamfer == true)
    {
        const chamferWidth = spec.endChamferWidth;
        if (chamferWidth >= dieLength)
        {
            throw regenError("End chamfer is larger than the die length.", ["dieChamferWidth"]);
        }
        const chamfer = createDieEndChamfer(context, id + "endChamfer", localCoordSys, crestStart, chamferWidth, spec.endChamferAngle);
        die = unionDieChamfer(context, id + "endChamferUnion", die, chamfer);
    }

    if (spec.partName is string)
    {
        setProperty(context, {
                    "entities" : die,
                    "propertyType" : PropertyType.NAME,
                    "value" : spec.partName
                });
    }
    if (spec.addMate == true)
    {
        addTapMateConnector(context, id + "mate", localCoordSys.origin, localCoordSys.xAxis, localCoordSys.zAxis, die);
    }
    return die;
}

function keptDieWantsThrough(form) returns boolean
{
    return form == KeptDieForm.THROUGH || form == KeptDieForm.BOTH;
}

function keptDieWantsThread(form) returns boolean
{
    return form == KeptDieForm.THREAD || form == KeptDieForm.BOTH;
}

function createThroughDieParts(context is Context, id is Id, spec is map)
{
    const localCoordSys = spec.localCoordSys;
    const dieLength = spec.dieLength;
    var threadSpec = spec;
    threadSpec.endChamfer = false;
    threadSpec.addMate = false;
    threadSpec.partName = spec.namePrefix is string ? spec.namePrefix ~ " External Through Die" : "External Through Die";
    const thread = createThreadedDie(context, id + "thread", threadSpec);
    addTapMateConnector(context, id + "threadStart", localCoordSys.origin, localCoordSys.xAxis, localCoordSys.zAxis, thread);
    addTapMateConnector(context, id + "threadEnd", localCoordSys.origin + dieLength * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, thread);

    if (spec.endChamfer == true)
    {
        const crestStart = dieCrestRadiusAt(spec, 0 * meter);
        const chamfer = createDieEndChamfer(context, id + "chamfer", localCoordSys, crestStart, spec.endChamferWidth, spec.endChamferAngle);
        setTapPartName(context, chamfer, spec.namePrefix is string ? spec.namePrefix ~ " External Through Die Chamfer" : "External Through Die Chamfer");
        addTapMateConnector(context, id + "chamferTop", localCoordSys.origin, localCoordSys.xAxis, localCoordSys.zAxis, chamfer);
    }
}

function createHoleCounterboreDeeper(context is Context, id is Id, localCoordSys is CoordSystem, height is ValueWithUnits, deeper is ValueWithUnits, boreRadius is ValueWithUnits) returns Query
{
    const overlap = 0.05 * millimeter;
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(0 * meter, height - overlap),
                vector(boreRadius, height - overlap),
                vector(boreRadius, height + deeper),
                vector(0 * meter, height + deeper),
                vector(0 * meter, height - overlap)
            ]);
}

function createHoleCounterboreBack(context is Context, id is Id, localCoordSys is CoordSystem, height is ValueWithUnits, back is ValueWithUnits, boreRadius is ValueWithUnits, wallAngle is ValueWithUnits) returns Query
{
    const slope = tan(wallAngle);
    const overlap = 0.05 * millimeter;
    const zCyl = height - back;
    const coneLength = slope > 0 ? boreRadius / slope : 0 * meter;
    const zApex = zCyl - coneLength;
    const zEnd = height + overlap;
    if (zApex >= 0 * meter)
    {
        return revolveAxisProfile(context, id, localCoordSys, [
                    vector(0 * meter, zApex),
                    vector(boreRadius, zCyl),
                    vector(boreRadius, zEnd),
                    vector(0 * meter, zEnd),
                    vector(0 * meter, zApex)
                ]);
    }
    const rAtEntry = max(boreRadius - zCyl * slope, 0 * meter);
    return revolveAxisProfile(context, id, localCoordSys, [
                vector(0 * meter, 0 * meter),
                vector(rAtEntry, 0 * meter),
                vector(boreRadius, zCyl),
                vector(boreRadius, zEnd),
                vector(0 * meter, zEnd),
                vector(0 * meter, 0 * meter)
            ]);
}

function subtractHoleFinishTool(context is Context, id is Id, tool is Query, part is Query, errorFields is array) returns Query
{
    try
    {
        opBoolean(context, id + "cut", {
                    "tools" : tool,
                    "targets" : part,
                    "operationType" : BooleanOperationType.SUBTRACTION
                });
    }
    catch
    {
        throw regenError("Could not add the hole counterbore.", errorFields);
    }
    const bored = qOwnerBody(qCreatedBy(id + "cut", EntityType.FACE));
    if (!isQueryEmpty(context, bored))
    {
        return bored;
    }
    return part;
}

function applyInternalHoleFinish(context is Context, id is Id, definition is map, part is Query, localCoordSys is CoordSystem, startRadius is ValueWithUnits, endRadius is ValueWithUnits, height is ValueWithUnits, depth is ValueWithUnits) returns Query
{
    var finished = part;
    if (wantsInternalStartChamfer(definition))
    {
        const chamferWidth = internalStartChamferWidth(definition);
        const chamferAngle = internalStartChamferAngle(definition);
        if (chamferWidth >= height)
        {
            throw regenError("Start chamfer is larger than the hole depth.", ["startChamferWidth"]);
        }
        const entryRadius = threadRadiusAt(startRadius, endRadius, height, 0 * meter, 0 * meter);
        const chamfer = createStartChamfer(context, id + "holeChamfer", localCoordSys, entryRadius, chamferWidth, chamferAngle, height);
        try
        {
            opBoolean(context, id + "holeChamferCut", {
                        "tools" : chamfer,
                        "targets" : finished,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
        catch
        {
            throw regenError("Could not add the hole start chamfer.", ["startChamferWidth", "startChamferAngle"]);
        }
        const chamfered = qOwnerBody(qCreatedBy(id + "holeChamferCut", EntityType.FACE));
        if (!isQueryEmpty(context, chamfered))
        {
            finished = chamfered;
        }
    }

    if (wantsInternalCounterbore(definition))
    {
        const wallAngle = definition.wallAngle is ValueWithUnits ? definition.wallAngle : 45 * degree;
        const holeEndRadius = threadRadiusAt(startRadius, endRadius, height, height, 0 * meter);
        const boreRadius = holeEndRadius + depth;
        const back = fittedCounterboreBack(internalCounterboreBack(definition), height, true);
        const deeper = internalCounterboreDeeper(definition);
        const errorFields = ["counterboreBack", "counterboreDeeper"];
        if (deeper > TOLERANCE.zeroLength * meter)
        {
            const deeperTool = createHoleCounterboreDeeper(context, id + "holeDeeper", localCoordSys, height, deeper, boreRadius);
            finished = subtractHoleFinishTool(context, id + "holeDeeper", deeperTool, finished, errorFields);
        }
        if (back > TOLERANCE.zeroLength * meter)
        {
            const backTool = createHoleCounterboreBack(context, id + "holeBack", localCoordSys, height, back, boreRadius, wallAngle);
            finished = subtractHoleFinishTool(context, id + "holeBack", backTool, finished, errorFields);
        }
    }
    return finished;
}

function tapSpecFromDefinition(definition is map, oriented is map, height is ValueWithUnits, pitch is ValueWithUnits, overlap is ValueWithUnits, extraRevs is number, outerHalfWidth is ValueWithUnits, rootHalfWidth is ValueWithUnits, truncation is ValueWithUnits, depth is ValueWithUnits, cutsInward is boolean, clearance is ValueWithUnits) returns map
{
    return {
            "startRadius" : oriented.startRadius,
            "endRadius" : oriented.endRadius,
            "height" : height,
            "pitch" : pitch,
            "threadClearance" : clearance,
            "counterboreDeeper" : internalCounterboreDeeper(definition),
            "counterboreBack" : internalCounterboreBack(definition),
            "overlap" : overlap,
            "extraRevs" : extraRevs,
            "outerHalfWidth" : outerHalfWidth,
            "rootHalfWidth" : rootHalfWidth,
            "truncation" : truncation,
            "depth" : depth,
            "cutsInward" : cutsInward,
            "leftHanded" : definition.leftHanded == true,
            "useCounterbore" : wantsInternalCounterbore(definition),
            "startChamfer" : wantsInternalStartChamfer(definition),
            "startChamferWidth" : internalStartChamferWidth(definition),
            "startChamferAngle" : internalStartChamferAngle(definition)
        };
}

function dieSpecFromDefinition(definition is map, oriented is map, height is ValueWithUnits, pitch is ValueWithUnits, overlap is ValueWithUnits, extraRevs is number, outerHalfWidth is ValueWithUnits, rootHalfWidth is ValueWithUnits, truncation is ValueWithUnits, depth is ValueWithUnits, clearance is ValueWithUnits) returns map
{
    return {
            "startRadius" : oriented.startRadius,
            "endRadius" : oriented.endRadius,
            "height" : height,
            "pitch" : pitch,
            "threadClearance" : clearance,
            "overlap" : overlap,
            "extraRevs" : extraRevs,
            "outerHalfWidth" : outerHalfWidth,
            "rootHalfWidth" : rootHalfWidth,
            "truncation" : truncation,
            "depth" : depth,
            "leftHanded" : definition.leftHanded == true,
            "endChamfer" : wantsExternalEndChamfer(definition),
            "endChamferWidth" : externalChamferWidth(definition),
            "endChamferAngle" : externalChamferAngle(definition),
            "fitThreadOnly" : true
        };
}

function applyTapPointFinish(spec is map, definition is map, borePoint is map, specifyEnd is boolean) returns map
{
    if (specifyEnd != true)
    {
        return spec;
    }
    spec.useCounterbore = pointBoolean(borePoint, "pointCounterbore", wantsInternalCounterbore(definition)) && holeUsesCounterbore(specifyEnd, borePoint);
    spec.startChamfer = pointBoolean(borePoint, "pointStartChamfer", wantsInternalStartChamfer(definition));
    spec.counterboreDeeper = pointLength(borePoint, "pointCounterboreDeeper", spec.counterboreDeeper);
    spec.counterboreBack = pointLength(borePoint, "pointCounterboreBack", spec.counterboreBack);
    spec.startChamferWidth = pointLength(borePoint, "pointStartChamferWidth", spec.startChamferWidth);
    spec.startChamferAngle = pointLength(borePoint, "pointStartChamferAngle", spec.startChamferAngle);
    return spec;
}

function applyDiePointFinish(spec is map, definition is map, borePoint is map, specifyEnd is boolean) returns map
{
    if (specifyEnd != true)
    {
        return spec;
    }
    spec.endChamfer = pointBoolean(borePoint, "pointEndChamfer", wantsExternalEndChamfer(definition));
    spec.endChamferWidth = pointLength(borePoint, "pointEndChamferWidth", spec.endChamferWidth);
    spec.endChamferAngle = pointLength(borePoint, "pointEndChamferAngle", spec.endChamferAngle);
    return spec;
}

function cutTapLocations(context is Context, id is Id, definition is map, locations is array, pointItems is array, clockItems is array, specifyEnd is boolean, oppositeDefault is boolean, clockOn is boolean, height is ValueWithUnits, tapSpec is map, kind is string, exclude is Query) returns array
{
    var skipped = [];
    var pointIndex = 0;
    for (var location in locations)
    {
        const label = kind ~ " " ~ (pointIndex + 1);
        const opId = id + "t" + pointIndex;
        try
        {
            const borePoint = borePointSettingsAt(pointItems, pointIndex, height, oppositeDefault);
            const opposite = specifyEnd ? borePoint.pointOpposite : oppositeDefault;
            const located = borePointLocation(context, location, opposite, exclude);
            const holeDepth = specifyEnd ? computeBoreDepth(context, borePoint, located, qNothing()) : height;
            const clockPoint = clockPointSettingsAt(clockItems, pointIndex);
            const clockAngle = clockOn ? signedClockAngle(clockPoint) : 0 * degree;
            var holeSpec = applyTapPointFinish(tapSpec, definition, borePoint, specifyEnd);
            const alignedTap = alignGeneratedPlacement(holeSpec, clockedCoordSystem(located.coordSystem, clockAngle));
            holeSpec.localCoordSys = alignedTap.coordSystem;
            holeSpec.leftHanded = alignedTap.leftHanded;
            holeSpec.tapLength = holeDepth;
            const tap = createThreadedTap(context, opId, holeSpec);
            const targets = placementTargets(context, tap, located.owner, exclude);
            if (isQueryEmpty(context, targets))
            {
                throw regenError("No part to cut at this location.");
            }
            opBoolean(context, opId + "place", {
                        "tools" : tap,
                        "targets" : targets,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
        catch
        {
            deleteCreatedSilent(context, opId);
            skipped = append(skipped, label);
        }
        pointIndex += 1;
    }
    return skipped;
}

function cutDieLocations(context is Context, id is Id, definition is map, locations is array, pointItems is array, clockItems is array, specifyEnd is boolean, oppositeDefault is boolean, clockOn is boolean, height is ValueWithUnits, dieSpec is map, kind is string, exclude is Query) returns array
{
    var skipped = [];
    var pointIndex = 0;
    for (var location in locations)
    {
        const label = kind ~ " " ~ (pointIndex + 1);
        const opId = id + "d" + pointIndex;
        try
        {
            const borePoint = borePointSettingsAt(pointItems, pointIndex, height, oppositeDefault);
            const opposite = specifyEnd ? borePoint.pointOpposite : oppositeDefault;
            const located = borePointLocation(context, location, opposite, exclude);
            const dieDepth = specifyEnd ? computeBoreDepth(context, borePoint, located, qNothing()) : height;
            const clockPoint = clockPointSettingsAt(clockItems, pointIndex);
            const clockAngle = clockOn ? signedClockAngle(clockPoint) : 0 * degree;
            var holeSpec = applyDiePointFinish(dieSpec, definition, borePoint, specifyEnd);
            const alignedDie = alignGeneratedPlacement(holeSpec, clockedCoordSystem(located.coordSystem, clockAngle));
            holeSpec.localCoordSys = alignedDie.coordSystem;
            holeSpec.leftHanded = alignedDie.leftHanded;
            holeSpec.dieLength = dieDepth;
            holeSpec.fitThreadOnly = false;
            if (!isQueryEmpty(context, located.owner))
            {
                holeSpec.outerRadius = max(resolvedDieOuterRadius(holeSpec), radialExtentInCSys(context, located.owner, holeSpec.localCoordSys) + 0.5 * millimeter);
            }
            const die = createThreadedDie(context, opId, holeSpec);
            const targets = placementTargets(context, die, located.owner, exclude);
            if (isQueryEmpty(context, targets))
            {
                throw regenError("No part to cut at this location.");
            }
            opBoolean(context, opId + "place", {
                        "tools" : die,
                        "targets" : targets,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
        catch
        {
            deleteCreatedSilent(context, opId);
            skipped = append(skipped, label);
        }
        pointIndex += 1;
    }
    return skipped;
}

annotation {
    "Feature Type Name" : "Printable Thread",
    "Feature Name Template" : "#name",
    "Icon" : printableThreadIcon::BLOB_DATA,
    "UIHint" : UIHint.CONTROL_VISIBILITY,
    "Manipulator Change Function" : "printableThreadManipulatorChange",
    "Editing Logic Function" : "printableThreadEditLogic"
}
export const printableThread = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Name" }
        definition.name is string;

        annotation {
            "Name" : "Surface",
            "Filter" : EntityType.FACE && QueryFilterCompound.ALLOWS_AXIS && SketchObject.NO && ConstructionObject.NO && ModifiableEntityOnly.YES,
            "MaxNumberOfPicks" : 1
        }
        definition.surface is Query;

        annotation {
            "Name" : "Internal start",
            "UIHint" : UIHint.ALWAYS_HIDDEN,
            "Default" : false
        }
        definition.primaryIsInternal is boolean;

        annotation {
            "Name" : "Stop at",
            "Description" : "Optional planes that stop the thread so it does not cut into an attached face. The side that contains the selected thread is kept.",
            "Filter" : QueryFilterCompound.ALLOWS_PLANE
        }
        definition.stopAt is Query;

        annotation {
            "Name" : "Dependent",
            "Default" : ThreadDependentParameter.FLAT,
            "UIHint" : UIHint.ALWAYS_HIDDEN
        }
        definition.dependentParameter is ThreadDependentParameter;

        annotation {
            "Name" : "Wall angle",
            "Description" : "Flank angle in a plane containing the axis (the print direction), measured from the plane perpendicular to the axis. 45 degrees prints without support when the axis is vertical."
        }
        isAngle(definition.wallAngle, PRINTABLE_THREAD_WALL_ANGLE_BOUNDS);

        if (definition.dependentParameter != ThreadDependentParameter.PITCH)
        {
            annotation {
                "Name" : "Pitch",
                "Description" : "Axial distance of one thread revolution."
            }
            isLength(definition.pitch, PRINTABLE_THREAD_PITCH_BOUNDS);
        }

        if (definition.dependentParameter != ThreadDependentParameter.DEPTH)
        {
            annotation {
                "Name" : "Thread depth",
                "Description" : "How far the groove cuts into the part. Remaining pitch becomes the crest flat."
            }
            isLength(definition.depth, PRINTABLE_THREAD_DEPTH_BOUNDS);
        }

        if (definition.dependentParameter != ThreadDependentParameter.TRUNCATION)
        {
            annotation {
                "Name" : "Truncation",
                "Description" : "Width of the flat at the groove root. Depth is measured to this flat."
            }
            isLength(definition.truncation, PRINTABLE_THREAD_TRUNCATION_BOUNDS);
        }

        if (definition.dependentParameter != ThreadDependentParameter.FLAT)
        {
            annotation {
                "Name" : "Flat length",
                "Description" : "Width of the remaining crest between adjacent grooves."
            }
            isLength(definition.flatLength, PRINTABLE_THREAD_FLAT_BOUNDS);
        }

        annotation {
            "Name" : "Dependent",
            "Default" : ThreadDependentParameter.FLAT,
            "Description" : "The selected value is calculated from the other three and the wall angle."
        }
        definition.dependentSelector is ThreadDependentParameter;

        if (definition.dependentParameter == ThreadDependentParameter.PITCH)
        {
            annotation {
                "Name" : "Pitch",
                "Description" : "Axial distance of one thread revolution.",
                "UIHint" : UIHint.READ_ONLY
            }
            isLength(definition.pitchCalculated, PRINTABLE_THREAD_PITCH_BOUNDS);
        }

        if (definition.dependentParameter == ThreadDependentParameter.DEPTH)
        {
            annotation {
                "Name" : "Thread depth",
                "Description" : "How far the groove cuts into the part. Remaining pitch becomes the crest flat.",
                "UIHint" : UIHint.READ_ONLY
            }
            isLength(definition.depthCalculated, PRINTABLE_THREAD_DEPTH_BOUNDS);
        }

        if (definition.dependentParameter == ThreadDependentParameter.TRUNCATION)
        {
            annotation {
                "Name" : "Truncation",
                "Description" : "Width of the flat at the groove root. Depth is measured to this flat.",
                "UIHint" : UIHint.READ_ONLY
            }
            isLength(definition.truncationCalculated, PRINTABLE_THREAD_TRUNCATION_BOUNDS);
        }

        if (definition.dependentParameter == ThreadDependentParameter.FLAT)
        {
            annotation {
                "Name" : "Flat length",
                "Description" : "Width of the remaining crest between adjacent grooves.",
                "UIHint" : UIHint.READ_ONLY
            }
            isLength(definition.flatLengthCalculated, PRINTABLE_THREAD_FLAT_BOUNDS);
        }

        annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
        definition.oppositeDirection is boolean;

        annotation { "Name" : "Left-handed", "Default" : false }
        definition.leftHanded is boolean;

        annotation { "Name" : "Surface known", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.primaryKnown is boolean;

        annotation { "Name" : "Unified finishes", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.unifiedFinishes is boolean;

        annotation { "Name" : "Mating thread", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.boreMatingPart is boolean;

        annotation {
            "Name" : "Parts",
            "UIHint" : UIHint.ALWAYS_HIDDEN,
            "Filter" : EntityType.BODY && BodyType.SOLID && SketchObject.NO && ConstructionObject.NO && ModifiableEntityOnly.YES
        }
        definition.boreParts is Query;

        annotation { "Name" : "Hole counterbore", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.holeCounterbore is boolean;

        annotation { "Name" : "Hole counterbore deeper", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isLength(definition.holeCounterboreDeeper, PRINTABLE_THREAD_COUNTERBORE_DEEPER_BOUNDS);

        annotation { "Name" : "Hole counterbore back", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isLength(definition.holeCounterboreBack, PRINTABLE_THREAD_COUNTERBORE_BACK_BOUNDS);

        annotation { "Name" : "Hole start chamfer", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.holeStartChamfer is boolean;

        annotation { "Name" : "Hole start chamfer width", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isLength(definition.holeStartChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

        annotation { "Name" : "Hole start chamfer angle", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isAngle(definition.holeStartChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);

        annotation { "Name" : "Die outer radius", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isLength(definition.dieOuterRadius, PRINTABLE_THREAD_DIE_OUTER_BOUNDS);

        annotation { "Name" : "Die end chamfer", "UIHint" : UIHint.ALWAYS_HIDDEN, "Default" : false }
        definition.dieEndChamfer is boolean;

        annotation { "Name" : "Die chamfer width", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isLength(definition.dieChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

        annotation { "Name" : "Die chamfer angle", "UIHint" : UIHint.ALWAYS_HIDDEN }
        isAngle(definition.dieChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);

        annotation { "Group Name" : "Internal thread", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Counterbore", "Default" : true }
            definition.counterbore is boolean;

            if (definition.counterbore)
            {
                annotation {
                    "Name" : "Counterbore deeper",
                    "Description" : "How far past the thread bottom to hollow out the full cylinder."
                }
                isLength(definition.counterboreDeeper, PRINTABLE_THREAD_COUNTERBORE_DEEPER_BOUNDS);

                annotation {
                    "Name" : "Counterbore back",
                    "Description" : "How much thread to remove back from the bottom as a plain cylinder."
                }
                isLength(definition.counterboreBack, PRINTABLE_THREAD_COUNTERBORE_BACK_BOUNDS);
            }

            annotation { "Name" : "Start chamfer", "Default" : false }
            definition.startChamfer is boolean;

            if (definition.startChamfer)
            {
                annotation {
                    "Name" : "Chamfer width",
                    "Description" : "Distance from the sharp corner along the hole wall."
                }
                isLength(definition.startChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

                annotation {
                    "Name" : "Chamfer angle",
                    "Description" : "Angle of the chamfer from the entry face. 45 degrees is equal on both faces."
                }
                isAngle(definition.startChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
            }
        }

        annotation { "Group Name" : "External thread", "Collapsed By Default" : false }
        {
            annotation { "Name" : "End chamfer", "Default" : false }
            definition.chamferEnd is boolean;

            if (definition.chamferEnd)
            {
                annotation {
                    "Name" : "Chamfer width",
                    "Description" : "Distance from the sharp corner along the pin or threaded face."
                }
                isLength(definition.chamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

                annotation {
                    "Name" : "Chamfer angle",
                    "Description" : "Angle of the chamfer from the end face. 45 degrees is equal on both faces."
                }
                isAngle(definition.chamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
            }
        }

        annotation {
            "Name" : "Thread clearance",
            "Description" : "Radial offset applied only to the mating thread tool. The initial surface stays exact."
        }
        isLength(definition.threadClearance, PRINTABLE_THREAD_CLEARANCE_BOUNDS);

        annotation { "Name" : "Initial thread", "UIHint" : UIHint.READ_ONLY }
        definition.generatedRole is string;

        annotation {
            "Name" : "Initial locations",
            "Description" : "Sketch points or mate connectors for more threads of the initial type.",
            "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR
        }
        definition.generatedLocations is Query;

        annotation { "Name" : "Configure initial locations", "Default" : false }
        definition.specifyGeneratedEnd is boolean;

        if (definition.specifyGeneratedEnd)
        {
            annotation { "Group Name" : "Initial locations", "Collapsed By Default" : false, "Driving Parameter" : "specifyGeneratedEnd" }
            {
                annotation {
                    "Name" : "Initial locations",
                    "Item name" : "Location",
                    "Driven query" : "genLocation",
                    "Item label template" : "#genLocation - #genEndType",
                    "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.ALLOW_ARRAY_FOCUS]
                }
                definition.generatedPoints is array;
                for (var generatedPoint in definition.generatedPoints)
                {
                    annotation {
                        "Name" : "Location",
                        "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR,
                        "MaxNumberOfPicks" : 1,
                        "UIHint" : UIHint.ALWAYS_HIDDEN
                    }
                    generatedPoint.genLocation is Query;

                    annotation { "Name" : "Label", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    generatedPoint.genLabel is string;

                    annotation { "Name" : "Key", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    generatedPoint.genKey is string;

                    annotation { "Name" : "End type", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                    generatedPoint.genEndType is BoreEndType;

                    annotation { "Name" : "Opposite direction", "UIHint" : [UIHint.OPPOSITE_DIRECTION, UIHint.MATCH_LAST_ARRAY_ITEM], "Default" : false }
                    generatedPoint.genOpposite is boolean;

                    annotation {
                        "Name" : "Length",
                        "Description" : "Used when End type is Blind.",
                        "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM
                    }
                    isLength(generatedPoint.genHoleDepth, PRINTABLE_THREAD_TAP_LENGTH_BOUNDS);

                    annotation {
                        "Name" : "End entity",
                        "Description" : "Used when End type is Up to face, part, or vertex.",
                        "Filter" : EntityType.FACE || EntityType.VERTEX || (EntityType.BODY && BodyType.SOLID && SketchObject.NO && ConstructionObject.NO),
                        "MaxNumberOfPicks" : 1,
                        "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM
                    }
                    generatedPoint.genEndEntity is Query;

                    if (definition.primaryKnown == true && definition.primaryIsInternal == true)
                    {
                        annotation { "Name" : "Counterbore", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM, "Default" : true }
                        generatedPoint.genCounterbore is boolean;

                        if (generatedPoint.genCounterbore)
                        {
                            annotation { "Name" : "Counterbore deeper", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(generatedPoint.genCounterboreDeeper, PRINTABLE_THREAD_COUNTERBORE_DEEPER_BOUNDS);

                            annotation { "Name" : "Counterbore back", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(generatedPoint.genCounterboreBack, PRINTABLE_THREAD_COUNTERBORE_BACK_BOUNDS);
                        }

                        annotation { "Name" : "Start chamfer", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM, "Default" : false }
                        generatedPoint.genStartChamfer is boolean;

                        if (generatedPoint.genStartChamfer)
                        {
                            annotation { "Name" : "Chamfer width", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(generatedPoint.genStartChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

                            annotation { "Name" : "Chamfer angle", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isAngle(generatedPoint.genStartChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
                        }
                    }

                    if (definition.primaryKnown == true && definition.primaryIsInternal != true)
                    {
                        annotation { "Name" : "End chamfer", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM, "Default" : false }
                        generatedPoint.genEndChamfer is boolean;

                        if (generatedPoint.genEndChamfer)
                        {
                            annotation { "Name" : "Chamfer width", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(generatedPoint.genEndChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

                            annotation { "Name" : "Chamfer angle", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isAngle(generatedPoint.genEndChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
                        }
                    }
                }
            }
        }

        if (definition.specifyGeneratedEnd != true)
        {
            annotation { "Name" : "Initial opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.generatedOpposite is boolean;
        }

        annotation {
            "Name" : "Clock initial threads",
            "Description" : "Rotate each initial-type cutter around its axis.",
            "Default" : false
        }
        definition.clockGenerated is boolean;

        if (definition.clockGenerated)
        {
            annotation { "Group Name" : "Initial clock", "Collapsed By Default" : false, "Driving Parameter" : "clockGenerated" }
            {
                annotation {
                    "Name" : "Initial clock",
                    "Item name" : "Location",
                    "Driven query" : "genClockLocation",
                    "Item label template" : "#genClockLocation - #genClockAngle #genClockSense",
                    "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.ALLOW_ARRAY_FOCUS]
                }
                definition.generatedClockPoints is array;
                for (var generatedClock in definition.generatedClockPoints)
                {
                    annotation {
                        "Name" : "Location",
                        "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR,
                        "MaxNumberOfPicks" : 1,
                        "UIHint" : UIHint.ALWAYS_HIDDEN
                    }
                    generatedClock.genClockLocation is Query;

                    annotation { "Name" : "Label", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    generatedClock.genClockLabel is string;

                    annotation { "Name" : "Key", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    generatedClock.genClockKey is string;

                    annotation {
                        "Name" : "Clocking angle",
                        "Description" : "How far to rotate the cutter around the thread axis, looking along the thread."
                    }
                    isAngle(generatedClock.genClockAngle, PRINTABLE_THREAD_CLOCK_BOUNDS);

                    annotation { "Name" : "Direction" }
                    generatedClock.genClockSense is ClockSense;
                }
            }
        }

        annotation { "Name" : "Mating thread", "UIHint" : UIHint.READ_ONLY }
        definition.matingRole is string;

        annotation {
            "Name" : "Mating locations",
            "Description" : "Sketch points or mate connectors for the mating thread.",
            "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR
        }
        definition.boreLocations is Query;

        annotation { "Name" : "Configure mating locations", "Default" : false }
        definition.specifyBoreEnd is boolean;

        if (definition.specifyBoreEnd)
        {
            annotation { "Group Name" : "Mating locations", "Collapsed By Default" : false, "Driving Parameter" : "specifyBoreEnd" }
            {
                annotation {
                    "Name" : "Mating locations",
                    "Item name" : "Location",
                    "Driven query" : "pointLocation",
                    "Item label template" : "#pointLocation - #pointEndType",
                    "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.ALLOW_ARRAY_FOCUS]
                }
                definition.borePoints is array;
                for (var borePoint in definition.borePoints)
                {
                    annotation {
                        "Name" : "Location",
                        "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR,
                        "MaxNumberOfPicks" : 1,
                        "UIHint" : UIHint.ALWAYS_HIDDEN
                    }
                    borePoint.pointLocation is Query;

                    annotation { "Name" : "Label", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    borePoint.pointLabel is string;

                    annotation { "Name" : "Key", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    borePoint.pointKey is string;

                    annotation { "Name" : "End type", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                    borePoint.pointEndType is BoreEndType;

                    annotation { "Name" : "Opposite direction", "UIHint" : [UIHint.OPPOSITE_DIRECTION, UIHint.MATCH_LAST_ARRAY_ITEM], "Default" : false }
                    borePoint.pointOpposite is boolean;

                    annotation {
                        "Name" : "Length",
                        "Description" : "Used when End type is Blind.",
                        "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM
                    }
                    isLength(borePoint.pointHoleDepth, PRINTABLE_THREAD_TAP_LENGTH_BOUNDS);

                    annotation {
                        "Name" : "End entity",
                        "Description" : "Used when End type is Up to face, part, or vertex.",
                        "Filter" : EntityType.FACE || EntityType.VERTEX || (EntityType.BODY && BodyType.SOLID && SketchObject.NO && ConstructionObject.NO),
                        "MaxNumberOfPicks" : 1,
                        "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM
                    }
                    borePoint.pointEndEntity is Query;

                    if (definition.primaryKnown == true && definition.primaryIsInternal != true)
                    {
                        annotation { "Name" : "Counterbore", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM, "Default" : true }
                        borePoint.pointCounterbore is boolean;

                        if (borePoint.pointCounterbore)
                        {
                            annotation { "Name" : "Counterbore deeper", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(borePoint.pointCounterboreDeeper, PRINTABLE_THREAD_COUNTERBORE_DEEPER_BOUNDS);

                            annotation { "Name" : "Counterbore back", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(borePoint.pointCounterboreBack, PRINTABLE_THREAD_COUNTERBORE_BACK_BOUNDS);
                        }

                        annotation { "Name" : "Start chamfer", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM, "Default" : false }
                        borePoint.pointStartChamfer is boolean;

                        if (borePoint.pointStartChamfer)
                        {
                            annotation { "Name" : "Chamfer width", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(borePoint.pointStartChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

                            annotation { "Name" : "Chamfer angle", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isAngle(borePoint.pointStartChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
                        }
                    }

                    if (definition.primaryKnown == true && definition.primaryIsInternal == true)
                    {
                        annotation { "Name" : "End chamfer", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM, "Default" : false }
                        borePoint.pointEndChamfer is boolean;

                        if (borePoint.pointEndChamfer)
                        {
                            annotation { "Name" : "Chamfer width", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isLength(borePoint.pointEndChamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

                            annotation { "Name" : "Chamfer angle", "UIHint" : UIHint.MATCH_LAST_ARRAY_ITEM }
                            isAngle(borePoint.pointEndChamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
                        }
                    }
                }
            }
        }

        if (definition.specifyBoreEnd != true)
        {
            annotation { "Name" : "Mating opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
            definition.boreOppositeDirection is boolean;
        }

        annotation {
            "Name" : "Clock mating threads",
            "Description" : "Rotate each mating cutter around its axis to correct printed thread engagement.",
            "Default" : false
        }
        definition.clockBores is boolean;

        if (definition.clockBores)
        {
            annotation { "Group Name" : "Mating clock", "Collapsed By Default" : false, "Driving Parameter" : "clockBores" }
            {
                annotation {
                    "Name" : "Mating clock",
                    "Item name" : "Location",
                    "Driven query" : "clockLocation",
                    "Item label template" : "#clockLocation - #clockAngle #clockSense",
                    "UIHint" : [UIHint.PREVENT_ARRAY_REORDER, UIHint.COLLAPSE_ARRAY_ITEMS, UIHint.ALLOW_ARRAY_FOCUS]
                }
                definition.clockPoints is array;
                for (var clockPoint in definition.clockPoints)
                {
                    annotation {
                        "Name" : "Location",
                        "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR,
                        "MaxNumberOfPicks" : 1,
                        "UIHint" : UIHint.ALWAYS_HIDDEN
                    }
                    clockPoint.clockLocation is Query;

                    annotation { "Name" : "Label", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    clockPoint.clockLabel is string;

                    annotation { "Name" : "Key", "UIHint" : UIHint.ALWAYS_HIDDEN }
                    clockPoint.clockKey is string;

                    annotation {
                        "Name" : "Clocking angle",
                        "Description" : "How far to rotate the cutter around the thread axis, looking along the thread."
                    }
                    isAngle(clockPoint.clockAngle, PRINTABLE_THREAD_CLOCK_BOUNDS);

                    annotation { "Name" : "Direction" }
                    clockPoint.clockSense is ClockSense;
                }
            }
        }

        annotation {
            "Name" : "Keep tools",
            "Description" : "Save the internal tap and external die as parts.",
            "Default" : false
        }
        definition.makeTap is boolean;

        if (definition.makeTap)
        {
            annotation {
                "Name" : "Tap form",
                "Description" : "Thread tap is one part of the surface length. Through tap keeps separate thread, chamfer, and counterbore parts."
            }
            definition.keptTapForm is KeptTapForm;

            annotation {
                "Name" : "Die form",
                "Description" : "Thread die is one part of the surface length. Through die keeps a separate thread and inverse-cone chamfer."
            }
            definition.keptDieForm is KeptDieForm;

            if (definition.keptTapForm == KeptTapForm.WITHOUT_COUNTERBORE || definition.keptTapForm == KeptTapForm.BOTH ||
                definition.keptDieForm == KeptDieForm.THROUGH || definition.keptDieForm == KeptDieForm.BOTH)
            {
                annotation {
                    "Name" : "Through length",
                    "Description" : "Threaded length of the through tap or through die."
                }
                isLength(definition.maxTapLength, PRINTABLE_THREAD_TAP_LENGTH_BOUNDS);
            }
        }
    }
    {
        const treeName = threadDisplayName(definition);
        setFeatureComputedParameter(context, id, {
                    "name" : "name",
                    "value" : treeName
                });
        setFeatureComputedParameter(context, id, {
                    "name" : "threadName",
                    "value" : treeName
                });
        const face = requireThreadFace(context, definition.surface);
        var part = qOwnerBody(face);
        const frame = threadSurfaceFrame(context, face, false);
        addManipulators(context, id, {
                    (DIRECTION_MANIPULATOR) : flipManipulator({
                            "base" : frame.localCoordSys.origin + frame.height / 2 * frame.localCoordSys.zAxis,
                            "direction" : frame.localCoordSys.zAxis,
                            "flipped" : definition.oppositeDirection
                        })
                });

        const oriented = threadSurfaceFrame(context, face, definition.oppositeDirection);
        const localCoordSys = oriented.localCoordSys;
        const height = oriented.height;
        const dims = threadProfileDimensions(definition);
        const pitch = dims.pitch;
        const depth = dims.depth;
        const truncation = dims.truncation;
        const dependent = dims.dependentParameter;
        const specifiedFields = specifiedThreadFields(dependent);
        const revolutions = height / pitch;
        const smallestRadius = min(oriented.startRadius, oriented.endRadius);
        const minCrest = max(pitch * 0.02, 0.02 * millimeter);
        const axialFlank = depth / tan(definition.wallAngle);
        const grooveWidth = truncation + 2 * axialFlank;
        const crestFlat = pitch - grooveWidth;
        if (depth <= TOLERANCE.zeroLength * meter)
        {
            throw regenError("Calculated thread depth is not positive. Increase pitch or wall angle, or reduce truncation or flat length.", specifiedFields);
        }
        if (pitch <= minCrest)
        {
            throw regenError("Calculated pitch is too small. Increase depth, truncation, or flat length, or reduce wall angle.", specifiedFields);
        }
        if (truncation < -TOLERANCE.zeroLength * meter)
        {
            throw regenError("Calculated truncation is not positive. Increase pitch or wall angle, or reduce depth or flat length.", specifiedFields);
        }
        if (depth >= smallestRadius)
        {
            if (dependent == ThreadDependentParameter.DEPTH)
            {
                throw regenError("Calculated thread depth is larger than the surface radius. Increase the radius, reduce pitch, or increase truncation or flat length.", specifiedFields);
            }
            throw regenError("Thread depth is larger than the surface radius. Increase the radius or reduce depth.", ["depth"]);
        }
        if (truncation >= pitch - minCrest)
        {
            throw regenError("Truncation is too large for this pitch.", dependent == ThreadDependentParameter.TRUNCATION || dependent == ThreadDependentParameter.PITCH ? specifiedFields : ["truncation", "pitch"]);
        }
        if (crestFlat <= minCrest)
        {
            throw regenError("Thread depth and truncation are too large for this pitch and wall angle. Reduce depth or truncation, or increase pitch or wall angle.", specifiedFields);
        }
        if (crestFlat < pitch * 0.1)
        {
            reportFeatureWarning(context, id, "The remaining crest will be very narrow. Reduce depth or truncation, or increase pitch.");
        }

        const startPoint = toWorld(localCoordSys, vector(oriented.startRadius, 0 * meter, 0 * meter));
        const cutsInward = threadCutsInward(context, face, localCoordSys, startPoint);
        const primaryIsInternal = !cutsInward;

        if (definition.chamferEnd && !primaryIsInternal)
        {
            if (definition.chamferWidth >= height)
            {
                throw regenError("Chamfer is larger than the surface length.", ["chamferWidth"]);
            }
            if (definition.chamferWidth >= smallestRadius)
            {
                throw regenError("Chamfer is larger than the surface radius.", ["chamferWidth"]);
            }

            const endEdges = directedEndEdges(context, face, localCoordSys, height);
            try
            {
                opChamfer(context, id + "chamfer", {
                            "entities" : endEdges,
                            "chamferType" : ChamferType.OFFSET_ANGLE,
                            "width" : definition.chamferWidth,
                            "angle" : definition.chamferAngle,
                            "oppositeDirection" : chamferWidthOnFace(context, endEdges, face)
                        });
            }
            catch
            {
                throw regenError("Could not chamfer the directed end.", ["chamferWidth", "chamferAngle"]);
            }
            part = qOwnerBody(qCreatedBy(id + "chamfer", EntityType.FACE));
        }

        const overlap = max(pitch * 0.02, 0.02 * millimeter);
        const halfWidths = threadProfileHalfWidths(overlap, depth, truncation, definition.wallAngle);
        const outerHalfWidth = halfWidths.outerHalfWidth;
        const rootHalfWidth = halfWidths.rootHalfWidth;
        const extraRevs = outerHalfWidth / pitch + 0.25;

        opHelix(context, id + "helix", {
                    "direction" : localCoordSys.zAxis,
                    "axisStart" : localCoordSys.origin,
                    "startPoint" : startPoint,
                    "interval" : [-extraRevs, revolutions + extraRevs],
                    "clockwise" : !definition.leftHanded,
                    "helicalPitch" : pitch,
                    "spiralPitch" : (oriented.endRadius - oriented.startRadius) / revolutions
                });

        const helixEdge = qCreatedBy(id + "helix", EntityType.EDGE);
        const startTangent = evEdgeTangentLine(context, {
                    "edge" : helixEdge,
                    "parameter" : 0,
                    "arcLengthParameterization" : false
                });
        const intoMaterial = intoMaterialDirection(localCoordSys, startTangent.origin, cutsInward);
        const profilePlane = threadProfilePlane(startTangent.origin, intoMaterial, localCoordSys.zAxis);
        const profile = newSketchOnPlane(context, id + "profile", {
                    "sketchPlane" : profilePlane
                });
        skPolyline(profile, "profile", {
                    "points" : threadProfilePoints(overlap, depth, outerHalfWidth, rootHalfWidth, truncation)
                });
        skSolve(profile);

        try
        {
            var sweepDefinition = {
                        "profiles" : qSketchRegion(id + "profile"),
                        "path" : helixEdge
                    };
            const lockFaces = threadLockFaces(face);
            if (!isQueryEmpty(context, lockFaces))
            {
                sweepDefinition.lockFaces = lockFaces;
            }
            opSweep(context, id + "sweep", sweepDefinition);
        }
        catch
        {
            throw regenError("Could not sweep the thread profile along the helix.");
        }

        var sweepTool = qCreatedBy(id + "sweep", EntityType.BODY);
        if (primaryIsInternal)
        {
            sweepTool = clipThreadSweepToFace(context, id + "faceClip", sweepTool, localCoordSys, height);
        }
        if (definition.stopAt is Query && !isQueryEmpty(context, definition.stopAt))
        {
            sweepTool = clipThreadSweepAtStopPlanes(context, id, sweepTool, definition.stopAt, localCoordSys, height);
        }

        touchLegacyPartPickHelpers(context, id + "legacy", qNothing(), localCoordSys, height);
        const matingClearance = definition.threadClearance is ValueWithUnits ? definition.threadClearance : 0 * meter;
        const tapClearance = primaryIsInternal ? 0 * meter : matingClearance;
        const dieClearance = primaryIsInternal ? matingClearance : 0 * meter;
        var exactTap = tapSpecFromDefinition(definition, oriented, height, pitch, overlap, extraRevs, outerHalfWidth, rootHalfWidth, truncation, depth, primaryIsInternal != true, tapClearance);
        exactTap.generatorCSys = localCoordSys;
        exactTap.alignHalfTurn = false;
        const matingTap = tapSpecFromDefinition(definition, oriented, height, pitch, overlap, extraRevs, outerHalfWidth, rootHalfWidth, truncation, depth, true, matingClearance);
        var exactDie = dieSpecFromDefinition(definition, oriented, height, pitch, overlap, extraRevs, outerHalfWidth, rootHalfWidth, truncation, depth, dieClearance);
        exactDie.matchExternal = primaryIsInternal != true;
        exactDie.generatorCSys = localCoordSys;
        exactDie.alignHalfTurn = false;
        const matingDie = dieSpecFromDefinition(definition, oriented, height, pitch, overlap, extraRevs, outerHalfWidth, rootHalfWidth, truncation, depth, matingClearance);
        try
        {
            opBoolean(context, id + "cut", {
                        "tools" : sweepTool,
                        "targets" : part,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
        catch
        {
            throw regenError("Could not cut the thread from the part.");
        }
        const cutPart = qOwnerBody(qCreatedBy(id + "cut", EntityType.FACE));
        if (!isQueryEmpty(context, cutPart))
        {
            part = cutPart;
        }
        if (primaryIsInternal)
        {
            applyInternalHoleFinish(context, id, definition, part, localCoordSys, oriented.startRadius, oriented.endRadius, height, depth);
        }

        const generatedLocations = queryLocations(context, definition.generatedLocations);
        const matingLocations = queryLocations(context, definition.boreLocations);
        const generatedPointItems = definition.specifyGeneratedEnd == true && definition.generatedPoints is array ? generatedPointsAsBorePoints(definition.generatedPoints) : [];
        const generatedClockItems = definition.clockGenerated == true && definition.generatedClockPoints is array ? generatedClocksAsClockPoints(definition.generatedClockPoints) : [];
        const matingPointItems = definition.specifyBoreEnd == true && definition.borePoints is array ? definition.borePoints : [];
        const matingClockItems = definition.clockBores == true && definition.clockPoints is array ? definition.clockPoints : [];
        var skipped = [];
        if (primaryIsInternal)
        {
            for (var label in cutTapLocations(context, id + "gen", definition, generatedLocations, generatedPointItems, generatedClockItems, definition.specifyGeneratedEnd == true, definition.generatedOpposite == true, definition.clockGenerated == true, height, exactTap, "Internal", part))
            {
                skipped = append(skipped, label);
            }
            for (var label in cutDieLocations(context, id + "mate", definition, matingLocations, matingPointItems, matingClockItems, definition.specifyBoreEnd == true, definition.boreOppositeDirection == true, definition.clockBores == true, height, matingDie, "External", part))
            {
                skipped = append(skipped, label);
            }
        }
        else
        {
            for (var label in cutDieLocations(context, id + "gen", definition, generatedLocations, generatedPointItems, generatedClockItems, definition.specifyGeneratedEnd == true, definition.generatedOpposite == true, definition.clockGenerated == true, height, exactDie, "External", part))
            {
                skipped = append(skipped, label);
            }
            for (var label in cutTapLocations(context, id + "mate", definition, matingLocations, matingPointItems, matingClockItems, definition.specifyBoreEnd == true, definition.boreOppositeDirection == true, definition.clockBores == true, height, matingTap, "Internal", part))
            {
                skipped = append(skipped, label);
            }
        }
        if (size(skipped) > 0)
        {
            reportFeatureWarning(context, id, "Skipped locations that could not be cut: " ~ joinLabels(skipped) ~ ".");
        }

        if (definition.makeTap == true)
        {
            const tapForm = definition.keptTapForm is KeptTapForm ? definition.keptTapForm : KeptTapForm.WITH_COUNTERBORE;
            const dieForm = definition.keptDieForm is KeptDieForm ? definition.keptDieForm : KeptDieForm.THREAD;
            const namePrefix = threadDisplayName(definition);
            if (keptTapWantsThread(tapForm))
            {
                var savedTap = exactTap;
                savedTap.localCoordSys = threadToolCoordSystem(localCoordSys, savedTap);
                savedTap.tapLength = height;
                savedTap.strictCounterbore = true;
                savedTap.partName = prefixedPartName(definition, "Internal Tap");
                savedTap.addMate = true;
                createThreadedTap(context, id + "tap", savedTap);
            }
            if (keptTapWantsThrough(tapForm))
            {
                var throughTap = exactTap;
                throughTap.localCoordSys = threadToolCoordSystem(localCoordSys, throughTap);
                throughTap.tapLength = definition.maxTapLength;
                throughTap.startChamferWidth = throughTapChamferWidth(definition);
                throughTap.startChamferAngle = throughTapChamferAngle(definition);
                throughTap.namePrefix = namePrefix;
                createThroughTapParts(context, id + "throughTap", throughTap);
            }
            if (keptDieWantsThread(dieForm))
            {
                const dieFrame = oppositeEndThreadFrame(localCoordSys, height, pitch, exactDie.leftHanded == true, exactDie.alignHalfTurn == true);
                var savedDie = exactDie;
                savedDie.localCoordSys = dieFrame.coordSystem;
                savedDie.leftHanded = dieFrame.leftHanded;
                savedDie.dieLength = height;
                savedDie.fitThreadOnly = true;
                savedDie.partName = prefixedPartName(definition, "External Die");
                savedDie.addMate = true;
                createThreadedDie(context, id + "die", savedDie);
            }
            if (keptDieWantsThrough(dieForm))
            {
                const throughDieFrame = oppositeEndThreadFrame(localCoordSys, height, pitch, exactDie.leftHanded == true, exactDie.alignHalfTurn == true);
                var throughDie = exactDie;
                throughDie.localCoordSys = throughDieFrame.coordSystem;
                throughDie.leftHanded = throughDieFrame.leftHanded;
                throughDie.dieLength = definition.maxTapLength;
                throughDie.fitThreadOnly = true;
                throughDie.namePrefix = namePrefix;
                createThroughDieParts(context, id + "throughDie", throughDie);
            }
        }

        opDeleteBodies(context, id + "deleteConstruction", {
                    "entities" : qUnion([
                            qCreatedBy(id + "helix", EntityType.BODY),
                            qCreatedBy(id + "profile", EntityType.BODY)
                        ])
                });
    }, {
            "name" : "Printable Thread",
            "borePoints" : [],
            "clockPoints" : [],
            "generatedPoints" : [],
            "generatedClockPoints" : [],
            "dependentParameter" : ThreadDependentParameter.FLAT,
            "dependentSelector" : ThreadDependentParameter.FLAT,
            "flatLength" : 1.4 * millimeter,
            "pitchCalculated" : 10 * millimeter,
            "depthCalculated" : 4 * millimeter,
            "truncationCalculated" : 0.6 * millimeter,
            "flatLengthCalculated" : 1.4 * millimeter,
            "counterbore" : true,
            "primaryIsInternal" : false,
            "primaryKnown" : false,
            "unifiedFinishes" : false,
            "holeCounterbore" : false,
            "holeStartChamfer" : false,
            "dieEndChamfer" : false,
            "dieOuterRadius" : 20 * millimeter,
            "keptTapForm" : KeptTapForm.WITH_COUNTERBORE,
            "keptDieForm" : KeptDieForm.THREAD,
            "generatedRole" : "Initial",
            "matingRole" : "Mating",
            "threadClearance" : 0.2 * millimeter,
            "holeCounterboreDeeper" : 1 * millimeter,
            "holeCounterboreBack" : 2 * millimeter,
            "holeStartChamferWidth" : 2 * millimeter,
            "holeStartChamferAngle" : 45 * degree,
            "dieChamferWidth" : 2 * millimeter,
            "dieChamferAngle" : 45 * degree
        });

export function printableThreadManipulatorChange(context is Context, definition is map, manipulators is map) returns map
{
    if (manipulators[DIRECTION_MANIPULATOR] is map)
    {
        definition.oppositeDirection = manipulators[DIRECTION_MANIPULATOR].flipped;
    }
    return definition;
}

export function printableThreadEditLogic(context is Context, id is Id, oldDefinition is map, definition is map, isCreating is boolean, specifiedParameters is map) returns map
{
    if (!(definition.name is string) || definition.name == "")
    {
        definition.name = "Printable Thread";
    }
    const treeName = threadDisplayName(definition);
    setFeatureComputedParameter(context, id, {
                "name" : "name",
                "value" : treeName
            });
    setFeatureComputedParameter(context, id, {
                "name" : "threadName",
                "value" : treeName
            });
    definition.primaryIsInternal = detectPrimaryIsInternal(context, definition.surface);
    definition.primaryKnown = false;
    if (definition.surface is Query && !isQueryEmpty(context, definition.surface) && size(evaluateQuery(context, definition.surface)) == 1)
    {
        const frame = try silent(threadSurfaceFrame(context, definition.surface, false));
        definition.primaryKnown = frame is map;
    }
    definition.generatedRole = locationRoleLabel(definition.primaryKnown == true, definition.primaryIsInternal == true, true);
    definition.matingRole = locationRoleLabel(definition.primaryKnown == true, definition.primaryIsInternal == true, false);
    if (definition.unifiedFinishes != true)
    {
        if (definition.primaryIsInternal == true)
        {
            definition.counterbore = definition.holeCounterbore == true;
            if (definition.holeCounterboreDeeper is ValueWithUnits)
            {
                definition.counterboreDeeper = definition.holeCounterboreDeeper;
            }
            if (definition.holeCounterboreBack is ValueWithUnits)
            {
                definition.counterboreBack = definition.holeCounterboreBack;
            }
            definition.startChamfer = definition.holeStartChamfer == true;
            if (definition.holeStartChamferWidth is ValueWithUnits)
            {
                definition.startChamferWidth = definition.holeStartChamferWidth;
            }
            if (definition.holeStartChamferAngle is ValueWithUnits)
            {
                definition.startChamferAngle = definition.holeStartChamferAngle;
            }
            if (definition.dieEndChamfer == true)
            {
                definition.chamferEnd = true;
                if (definition.dieChamferWidth is ValueWithUnits)
                {
                    definition.chamferWidth = definition.dieChamferWidth;
                }
                if (definition.dieChamferAngle is ValueWithUnits)
                {
                    definition.chamferAngle = definition.dieChamferAngle;
                }
            }
        }
        definition.unifiedFinishes = true;
    }
    const wantsThrough = keptTapWantsThrough(definition.keptTapForm) || keptDieWantsThrough(definition.keptDieForm);
    const oldWantsThrough = keptTapWantsThrough(oldDefinition.keptTapForm) || keptDieWantsThrough(oldDefinition.keptDieForm);
    if (definition.makeTap == true && wantsThrough && specifiedParameters.maxTapLength != true)
    {
        const justEnabledThrough = oldDefinition.makeTap != true || !oldWantsThrough;
        if (justEnabledThrough || !(definition.maxTapLength is ValueWithUnits))
        {
            definition.maxTapLength = threadLengthFromDefinition(context, definition);
        }
    }
    if (definition.specifyGeneratedEnd == true)
    {
        definition = syncGeneratedPointSettings(context, oldDefinition, definition, specifiedParameters);
    }
    if (definition.clockGenerated == true)
    {
        definition = syncGeneratedClockSettings(context, oldDefinition, definition, specifiedParameters);
    }
    if (definition.specifyBoreEnd == true)
    {
        definition = syncBorePointSettings(context, oldDefinition, definition, specifiedParameters);
    }
    if (definition.clockBores == true)
    {
        definition = syncClockPointSettings(context, oldDefinition, definition, specifiedParameters);
    }
    const selector = definition.dependentSelector is ThreadDependentParameter ? definition.dependentSelector : threadDependentParameter(definition);
    const previousDependent = threadDependentParameter(oldDefinition);
    if (specifiedParameters.dependentSelector == true && selector != previousDependent)
    {
        const previous = threadProfileDimensions(oldDefinition);
        definition.pitch = previous.pitch;
        definition.depth = previous.depth;
        definition.truncation = previous.truncation;
        definition.flatLength = previous.flatLength;
        definition.dependentParameter = selector;
    }
    else
    {
        definition.dependentSelector = threadDependentParameter(definition);
        definition.dependentParameter = threadDependentParameter(definition);
    }
    definition = applyDependentThreadDimension(definition);
    return definition;
}
