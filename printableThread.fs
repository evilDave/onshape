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

function solidsFromPartPicks(context is Context, picks is Query) returns Query
{
    var solids = [];
    for (var selection in evaluateQuery(context, picks))
    {
        var owner = qBodyType(selection, BodyType.SOLID);
        if (isQueryEmpty(context, owner))
        {
            owner = qBodyType(qOwnerBody(selection), BodyType.SOLID);
        }
        if (!isQueryEmpty(context, owner))
        {
            solids = append(solids, owner);
        }
    }
    return qUnion(evaluateQuery(context, qUnion(solids)));
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
            "pointEndEntity" : qNothing()
        };
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

function resolveBoreOwner(context is Context, candidate is Query, location is Query, origin is Vector) returns Query
{
    var owner = qUnion(evaluateQuery(context, qBodyType(candidate, BodyType.SOLID)));
    if (!isQueryEmpty(context, owner))
    {
        return owner;
    }
    owner = qUnion(evaluateQuery(context, qBodyType(qOwnerBody(location), BodyType.SOLID)));
    if (!isQueryEmpty(context, owner))
    {
        return owner;
    }
    for (var solid in evaluateQuery(context, qAllModifiableSolidBodies()))
    {
        if (!isQueryEmpty(context, qIntersection([location, qMateConnectorsOfParts(solid)])))
        {
            return solid;
        }
    }
    owner = qClosestTo(qAllModifiableSolidBodies(), origin);
    if (isQueryEmpty(context, owner))
    {
        throw regenError("The selected location must belong to a solid part.", ["boreLocations"]);
    }
    return qUnion(evaluateQuery(context, owner));
}

function borePointLocation(context is Context, location is Query, oppositeDirection is boolean) returns map
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
            "owner" : resolveBoreOwner(context, qNothing(), location, axis.origin)
        };
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

function createThreadedTap(context is Context, id is Id, spec is map) returns Query
{
    const localCoordSys = spec.localCoordSys;
    const tapLength = spec.tapLength;
    const tapRevs = tapLength / spec.pitch;
    const tapEndRadius = threadRadiusAt(spec.startRadius, spec.endRadius, spec.height, tapLength, spec.threadClearance);
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

    try
    {
        opBoolean(context, id + "cut", {
                    "tools" : qCreatedBy(id + "sweep", EntityType.BODY),
                    "targets" : tap,
                    "operationType" : BooleanOperationType.SUBTRACTION
                });
    }
    catch
    {
        throw regenError("Could not build the tap.");
    }
    if (isQueryEmpty(context, tap))
    {
        tap = qCreatedBy(id + "cut", EntityType.BODY);
    }

    if (spec.useCounterbore != false)
    {
        const counterboreBack = fittedCounterboreBack(spec.counterboreBack, tapLength, spec.strictCounterbore == true);
        const boreOverlap = max(counterboreBack, 0.05 * millimeter);
        const cutoutEnd = tapLength + spec.counterboreDeeper;
        if (cutoutEnd > tapLength - boreOverlap + TOLERANCE.zeroLength * meter)
        {
            const cutout = createCounterboreWithCone(context, id + "counterbore", localCoordSys, tapLength - boreOverlap, cutoutEnd, tapEndRadius, spec.depth);
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
        const entryRadius = threadRadiusAt(spec.startRadius, spec.endRadius, spec.height, 0 * meter, spec.threadClearance);
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
    threadSpec.partName = "Through tap thread";
    const thread = createThreadedTap(context, id + "thread", threadSpec);
    addTapMateConnector(context, id + "threadStart", localCoordSys.origin, localCoordSys.xAxis, localCoordSys.zAxis, thread);
    addTapMateConnector(context, id + "threadEnd", localCoordSys.origin + tapLength * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, thread);

    if (spec.startChamfer == true)
    {
        const entryRadius = threadRadiusAt(spec.startRadius, spec.endRadius, spec.height, 0 * meter, spec.threadClearance);
        const chamferDims = startChamferDimensions(entryRadius, spec.startChamferWidth, spec.startChamferAngle, tapLength);
        const chamfer = createStartChamfer(context, id + "chamfer", localCoordSys, entryRadius, spec.startChamferWidth, spec.startChamferAngle, tapLength);
        setTapPartName(context, chamfer, "Through tap chamfer");
        addTapMateConnector(context, id + "chamferTop", localCoordSys.origin - chamferDims.overlap * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, chamfer);
    }

    if (spec.useCounterbore == true)
    {
        const tapEndRadius = threadRadiusAt(spec.startRadius, spec.endRadius, spec.height, tapLength, spec.threadClearance);
        const counterboreBack = fittedCounterboreBack(spec.counterboreBack, tapLength, false);
        const boreOverlap = max(counterboreBack, 0.05 * millimeter);
        const cutoutEnd = tapLength + spec.counterboreDeeper;
        const counterbore = createCounterboreWithCone(context, id + "counterbore", localCoordSys, tapLength - boreOverlap, cutoutEnd, tapEndRadius, spec.depth);
        setTapPartName(context, counterbore, "Through tap counterbore");
        addTapMateConnector(context, id + "counterboreMeet", localCoordSys.origin + tapLength * localCoordSys.zAxis, localCoordSys.xAxis, localCoordSys.zAxis, counterbore);
    }
}

annotation {
    "Feature Type Name" : "Printable Thread",
    "Icon" : printableThreadIcon::BLOB_DATA,
    "UIHint" : UIHint.CONTROL_VISIBILITY,
    "Manipulator Change Function" : "printableThreadManipulatorChange",
    "Editing Logic Function" : "printableThreadEditLogic"
}
export const printableThread = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation {
            "Name" : "Surface",
            "Filter" : EntityType.FACE && QueryFilterCompound.ALLOWS_AXIS && SketchObject.NO && ConstructionObject.NO && ModifiableEntityOnly.YES,
            "MaxNumberOfPicks" : 1
        }
        definition.surface is Query;

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

        annotation { "Name" : "Chamfer end", "Default" : false }
        definition.chamferEnd is boolean;

        if (definition.chamferEnd)
        {
            annotation {
                "Name" : "Chamfer width",
                "Description" : "Distance from the sharp corner along the threaded face."
            }
            isLength(definition.chamferWidth, PRINTABLE_THREAD_CHAMFER_WIDTH_BOUNDS);

            annotation {
                "Name" : "Chamfer angle",
                "Description" : "Angle of the chamfer from the end face. 45 degrees is equal on both faces."
            }
            isAngle(definition.chamferAngle, PRINTABLE_THREAD_CHAMFER_ANGLE_BOUNDS);
        }

        annotation { "Name" : "Left-handed", "Default" : false }
        definition.leftHanded is boolean;

        annotation { "Name" : "Bore mating part", "Default" : false }
        definition.boreMatingPart is boolean;

        if (definition.boreMatingPart)
        {
            annotation {
                "Name" : "Thread clearance",
                "Description" : "Radial offset of the thread cross-section away from the axis. Flanks and flats stay parallel."
            }
            isLength(definition.threadClearance, PRINTABLE_THREAD_CLEARANCE_BOUNDS);

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

            annotation {
                "Name" : "Parts",
                "Description" : "Click any vertex, edge, or face of a part. The whole part is added.",
                "Filter" : ((EntityType.BODY && BodyType.SOLID) || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX) && SketchObject.NO && ConstructionObject.NO && ModifiableEntityOnly.YES
            }
            definition.boreParts is Query;

            annotation {
                "Name" : "Sketch points to place holes",
                "Description" : "Select sketch points, or use the mate connector button for a real or inferred mate. Click a mate to move or rotate it. Each hole is a blind bore of the original thread length unless you configure the holes.",
                "Filter" : EntityType.VERTEX && SketchObject.YES && ModifiableEntityOnly.YES || BodyType.MATE_CONNECTOR,
                "UIHint" : UIHint.INITIAL_FOCUS
            }
            definition.boreLocations is Query;

            annotation { "Name" : "Configure holes", "Default" : false }
            definition.specifyBoreEnd is boolean;

            if (definition.specifyBoreEnd)
            {
                annotation { "Group Name" : "Holes", "Collapsed By Default" : false, "Driving Parameter" : "specifyBoreEnd" }
                {
                    annotation {
                        "Name" : "Holes",
                        "Item name" : "Hole",
                        "Driven query" : "pointLocation",
                        "Item label template" : "#pointLocation - #pointEndType",
                        "Description" : "One row per selected point. The X resets that hole to a blind bore of the original thread length. Remove a hole from the sketch-point query.",
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
                            "Name" : "Hole depth",
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
                    }
                }
            }

            if (definition.specifyBoreEnd != true)
            {
                annotation { "Name" : "Opposite direction", "UIHint" : UIHint.OPPOSITE_DIRECTION, "Default" : false }
                definition.boreOppositeDirection is boolean;
            }

            annotation {
                "Name" : "Clock bores",
                "Description" : "Rotate each hole's tap to correct printed thread engagement.",
                "Default" : false
            }
            definition.clockBores is boolean;

            if (definition.clockBores)
            {
                annotation { "Group Name" : "Clock", "Collapsed By Default" : false, "Driving Parameter" : "clockBores" }
                {
                    annotation {
                        "Name" : "Clock",
                        "Item name" : "Hole",
                        "Driven query" : "clockLocation",
                        "Item label template" : "#clockLocation - #clockAngle #clockSense",
                        "Description" : "One row per selected point. New holes start at 0. The X resets that hole's clocking angle to 0.",
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
                            "Description" : "How far to rotate the tap around the hole axis, looking into the hole."
                        }
                        isAngle(clockPoint.clockAngle, PRINTABLE_THREAD_CLOCK_BOUNDS);

                        annotation { "Name" : "Direction" }
                        clockPoint.clockSense is ClockSense;
                    }
                }
            }

            annotation {
                "Name" : "Keep tap",
                "Description" : "Save the tap as a part. Hole bores always use a tap sized to each hole.",
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

                if (definition.keptTapForm == KeptTapForm.WITHOUT_COUNTERBORE || definition.keptTapForm == KeptTapForm.BOTH)
                {
                    annotation {
                        "Name" : "Tap length",
                        "Description" : "Threaded length of the through tap."
                    }
                    isLength(definition.maxTapLength, PRINTABLE_THREAD_TAP_LENGTH_BOUNDS);
                }
            }
        }
    }
    {
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

        if (definition.chamferEnd)
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

        const doBore = definition.boreMatingPart == true;
        const keepTap = definition.makeTap == true;
        if (doBore || keepTap)
        {
            const tapSpec = {
                    "startRadius" : oriented.startRadius,
                    "endRadius" : oriented.endRadius,
                    "height" : height,
                    "pitch" : pitch,
                    "threadClearance" : definition.threadClearance,
                    "counterboreDeeper" : definition.counterboreDeeper,
                    "counterboreBack" : definition.counterboreBack,
                    "overlap" : overlap,
                    "extraRevs" : extraRevs,
                    "outerHalfWidth" : outerHalfWidth,
                    "rootHalfWidth" : rootHalfWidth,
                    "truncation" : truncation,
                    "depth" : depth,
                    "cutsInward" : cutsInward,
                    "leftHanded" : definition.leftHanded == true,
                    "useCounterbore" : definition.counterbore != false,
                    "startChamfer" : definition.startChamfer == true,
                    "startChamferWidth" : definition.startChamferWidth,
                    "startChamferAngle" : definition.startChamferAngle
                };

            if (doBore)
            {
                const locations = definition.boreLocations is Query ? evaluateQuery(context, definition.boreLocations) : [];
                const pointItems = definition.specifyBoreEnd == true && definition.borePoints is array ? definition.borePoints : [];
                const clockItems = definition.clockBores == true && definition.clockPoints is array ? definition.clockPoints : [];
                const partPicks = definition.boreParts is Query ? definition.boreParts : qNothing();
                const pickedParts = qSubtraction(solidsFromPartPicks(context, partPicks), part);
                var cutTools = [];
                var cutTargets = qNothing();

                if (!isQueryEmpty(context, pickedParts))
                {
                    var inplaceSpec = tapSpec;
                    inplaceSpec.localCoordSys = localCoordSys;
                    inplaceSpec.tapLength = height;
                    const inplaceTap = createThreadedTap(context, id + "inplace", inplaceSpec);
                    const intersecting = collidingSolids(context, inplaceTap, pickedParts);
                    if (isQueryEmpty(context, intersecting) && size(locations) == 0)
                    {
                        throw regenError("The selected part does not intersect the tap.", ["boreParts"]);
                    }
                    if (size(evaluateQuery(context, intersecting)) < size(evaluateQuery(context, pickedParts)))
                    {
                        reportFeatureWarning(context, id, "Some selected parts do not intersect the tap and were skipped.");
                    }
                    if (isQueryEmpty(context, intersecting))
                    {
                        opDeleteBodies(context, id + "deleteInplace", {
                                    "entities" : inplaceTap
                                });
                    }
                    else
                    {
                        cutTools = append(cutTools, inplaceTap);
                        cutTargets = qUnion([cutTargets, intersecting]);
                    }
                }

                var pointIndex = 0;
                for (var location in locations)
                {
                    const borePoint = borePointSettingsAt(pointItems, pointIndex, height, definition.boreOppositeDirection);
                    const opposite = definition.specifyBoreEnd == true ? borePoint.pointOpposite : definition.boreOppositeDirection;
                    const located = borePointLocation(context, location, opposite);
                    var exclude = qNothing();
                    if (size(cutTools) > 0)
                    {
                        exclude = qUnion(cutTools);
                    }
                    const holeDepth = definition.specifyBoreEnd == true ? computeBoreDepth(context, borePoint, located, exclude) : height;
                    const clockPoint = clockPointSettingsAt(clockItems, pointIndex);
                    const clockAngle = definition.clockBores == true ? signedClockAngle(clockPoint) : 0 * degree;
                    var holeSpec = tapSpec;
                    holeSpec.localCoordSys = clockedCoordSystem(located.coordSystem, clockAngle);
                    holeSpec.tapLength = holeDepth;
                    holeSpec.useCounterbore = definition.counterbore != false && holeUsesCounterbore(definition.specifyBoreEnd == true, borePoint);
                    cutTools = append(cutTools, createThreadedTap(context, id + "h" + pointIndex, holeSpec));
                    cutTargets = qUnion([cutTargets, located.owner]);
                    pointIndex += 1;
                }

                if (size(cutTools) > 0)
                {
                    try
                    {
                        opBoolean(context, id + "matingCut", {
                                    "tools" : qUnion(cutTools),
                                    "targets" : cutTargets,
                                    "operationType" : BooleanOperationType.SUBTRACTION
                                });
                    }
                    catch
                    {
                        throw regenError("Could not bore the mating parts with the tap.");
                    }
                }
            }

            if (keepTap)
            {
                const form = definition.keptTapForm is KeptTapForm ? definition.keptTapForm : KeptTapForm.WITH_COUNTERBORE;
                if (keptTapWantsThread(form))
                {
                    var savedSpec = tapSpec;
                    savedSpec.localCoordSys = localCoordSys;
                    savedSpec.tapLength = height;
                    savedSpec.strictCounterbore = true;
                    savedSpec.useCounterbore = definition.counterbore != false;
                    savedSpec.partName = "Thread tap";
                    savedSpec.addMate = true;
                    createThreadedTap(context, id + "tap", savedSpec);
                }
                if (keptTapWantsThrough(form))
                {
                    var throughSpec = tapSpec;
                    throughSpec.localCoordSys = localCoordSys;
                    throughSpec.tapLength = definition.maxTapLength;
                    throughSpec.useCounterbore = definition.counterbore != false;
                    throughSpec.startChamfer = definition.startChamfer == true;
                    throughSpec.startChamferWidth = throughTapChamferWidth(definition);
                    throughSpec.startChamferAngle = throughTapChamferAngle(definition);
                    createThroughTapParts(context, id + "through", throughSpec);
                }
            }
        }

        try
        {
            opBoolean(context, id + "cut", {
                        "tools" : qCreatedBy(id + "sweep", EntityType.BODY),
                        "targets" : part,
                        "operationType" : BooleanOperationType.SUBTRACTION
                    });
        }
        catch
        {
            throw regenError("Could not cut the thread from the part.");
        }

        opDeleteBodies(context, id + "deleteConstruction", {
                    "entities" : qUnion([
                            qCreatedBy(id + "helix", EntityType.BODY),
                            qCreatedBy(id + "profile", EntityType.BODY)
                        ])
                });
    }, {
            "borePoints" : [],
            "clockPoints" : [],
            "dependentParameter" : ThreadDependentParameter.FLAT,
            "dependentSelector" : ThreadDependentParameter.FLAT,
            "flatLength" : 1.4 * millimeter,
            "pitchCalculated" : 10 * millimeter,
            "depthCalculated" : 4 * millimeter,
            "truncationCalculated" : 0.6 * millimeter,
            "flatLengthCalculated" : 1.4 * millimeter,
            "counterbore" : true
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
    if (specifiedParameters.boreParts == true && definition.boreParts is Query && !isQueryEmpty(context, definition.boreParts))
    {
        const parts = solidsFromPartPicks(context, definition.boreParts);
        if (!isQueryEmpty(context, parts))
        {
            definition.boreParts = parts;
        }
    }
    if (specifiedParameters.boreMatingPart == true && definition.boreMatingPart != true)
    {
        definition.makeTap = false;
    }
    if (definition.makeTap == true && keptTapWantsThrough(definition.keptTapForm) && specifiedParameters.maxTapLength != true)
    {
        const justEnabledThrough = oldDefinition.makeTap != true || !keptTapWantsThrough(oldDefinition.keptTapForm);
        if (justEnabledThrough || !(definition.maxTapLength is ValueWithUnits))
        {
            definition.maxTapLength = threadLengthFromDefinition(context, definition);
        }
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
