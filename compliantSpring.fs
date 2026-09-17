FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");

/**
 * Creates a planar compliant-spring centreline from one backbone and two guide
 * sketch curves. The guides are amplitude envelopes and the backbone supplies
 * the spring endpoints and zero crossings.
 *
 * The curves are matched by normalised arc length, with guide orientation
 * corrected automatically. Active-guide amplitude redistributes each half-wave
 * so larger lobes receive more length. Each generator emits a chain of cubic
 * Bezier sketch segments.
 *
 * Three generators share the same guide adherence and optional backbone
 * crossings. Sine follows sampled sinusoidal offsets, Flowing S interpolates
 * landmarks with shared tangents, and Zigzag joins them with straight flanks.
 * Sine exposes crest softness because short, high-amplitude waves have high
 * peak curvature. Flowing S exposes Loopiness, progressing from bounded
 * handles to deliberately unconstrained fold-back.
 * Forced crossings add zero-crossing landmarks; otherwise extrema connect
 * directly and cross the backbone naturally.
 *
 * Safe generators use bounded tangents and monotonic control polygons.
 * Endpoint incidence remains adjustable for smooth generators. Amplitude-aware
 * spacing gives larger lobes more backbone length to moderate crest curvature.
 */

export enum SpringCurveGenerator
{
    annotation { "Name" : "Sine" }
    SINE,

    annotation { "Name" : "Flowing S" }
    FLOWING_S,

    annotation { "Name" : "Zigzag" }
    ZIGZAG
}

const SPRING_NODE_BOUNDS =
{
    (unitless) : [1, 6, 100]
} as IntegerBoundSpec;

const SPRING_SAMPLES_PER_CYCLE_BOUNDS =
{
    (unitless) : [4, 16, 32]
} as IntegerBoundSpec;

const GUIDE_ADHERENCE_BOUNDS =
{
    (unitless) : [0, 1, 1]
} as RealBoundSpec;

const CREST_SOFTNESS_BOUNDS =
{
    (unitless) : [0, 0.5, 5]
} as RealBoundSpec;

const LOOPINESS_BOUNDS =
{
    (unitless) : [0, 0, 5]
} as RealBoundSpec;

const AMPLITUDE_SPACING_BOUNDS =
{
    (unitless) : [0, 0.5, 5]
} as RealBoundSpec;

const ENDPOINT_ANGLE_BOUNDS =
{
    (degree) : [-90, 0, 90],
    (radian) : 0
} as AngleBoundSpec;

function requireSingleEdge(context is Context, edgeQuery is Query, parameterName is string) returns Query
{
    const edges = evaluateQuery(context, edgeQuery);
    if (size(edges) != 1)
    {
        throw regenError("Select exactly one sketch edge.", [parameterName]);
    }
    return edges[0];
}

function edgeEnds(context is Context, edge is Query) returns array
{
    return evEdgeTangentLines(context, {
                "edge" : edge,
                "parameters" : [0, 1],
                "arcLengthParameterization" : true
            });
}

function requireOpenEdge(context is Context, edge is Query, parameterName is string) returns array
{
    const ends = edgeEnds(context, edge);
    if (norm(ends[1].origin - ends[0].origin) <= TOLERANCE.zeroLength * meter)
    {
        throw regenError("Select an open sketch edge.", [parameterName]);
    }
    return ends;
}

function guideIsReversed(guideEnds is array, backboneEnds is array) returns boolean
{
    const forwardDistance =
            squaredNorm(guideEnds[0].origin - backboneEnds[0].origin) +
            squaredNorm(guideEnds[1].origin - backboneEnds[1].origin);
    const reverseDistance =
            squaredNorm(guideEnds[1].origin - backboneEnds[0].origin) +
            squaredNorm(guideEnds[0].origin - backboneEnds[1].origin);
    return reverseDistance < forwardDistance;
}

function orientedParameters(parameters is array, reversed is boolean) returns array
{
    if (!reversed)
    {
        return parameters;
    }

    var result = [];
    for (var parameter in parameters)
    {
        result = append(result, 1 - parameter);
    }
    return result;
}

function tangentAtSample(points is array, sampleIndex is number, samplesPerSegment is number) returns Vector
{
    if (sampleIndex == 0)
    {
        return (points[1] - points[0]) * samplesPerSegment;
    }

    const lastIndex = size(points) - 1;
    if (sampleIndex == lastIndex)
    {
        return (points[lastIndex] - points[lastIndex - 1]) * samplesPerSegment;
    }

    return (points[sampleIndex + 1] - points[sampleIndex - 1]) * samplesPerSegment / 2;
}

function segmentBoundaryIndices(nodes is number, samplesPerCycle is number,
    forceBackboneCrossings is boolean) returns array
{
    const sampleCount = nodes * samplesPerCycle / 2;
    const quarterCycle = samplesPerCycle / 4;
    var indices = [0];
    for (var sampleIndex = quarterCycle; sampleIndex < sampleCount; sampleIndex += quarterCycle)
    {
        const quarterIndex = sampleIndex / quarterCycle;
        if (forceBackboneCrossings || quarterIndex % 2 == 1)
        {
            indices = append(indices, sampleIndex);
        }
    }
    return append(indices, sampleCount);
}

function landmarkTangent(points is array, pointIndex is number, handleRatio is number) returns Vector
{
    const lastIndex = size(points) - 1;
    if (pointIndex == 0)
    {
        return 3 * handleRatio * (points[1] - points[0]);
    }
    if (pointIndex == lastIndex)
    {
        return 3 * handleRatio * (points[lastIndex] - points[lastIndex - 1]);
    }

    const previousChord = points[pointIndex] - points[pointIndex - 1];
    const nextChord = points[pointIndex + 1] - points[pointIndex];
    const bisector = normalize(previousChord) + normalize(nextChord);
    const direction = norm(bisector) <= TOLERANCE.zeroLength
            ? normalize(nextChord)
            : normalize(bisector);
    return direction * 3 * handleRatio * min(norm(previousChord), norm(nextChord));
}

// Keep adjacent smooth segments on a shared tangent without letting narrow lobes create oversized handles.
function boundedTangentAtKnot(points is array, sampleIndex is number, samplesPerSegment is number) returns Vector
{
    const lastIndex = size(points) - 1;
    const point = points[sampleIndex];
    var tangent = tangentAtSample(points, sampleIndex, samplesPerSegment);
    var limitingChord;

    if (sampleIndex == 0)
    {
        limitingChord = points[samplesPerSegment] - point;
    }
    else if (sampleIndex == lastIndex)
    {
        limitingChord = point - points[lastIndex - samplesPerSegment];
    }
    else
    {
        const previousChord = point - points[sampleIndex - samplesPerSegment];
        const nextChord = points[sampleIndex + samplesPerSegment] - point;
        limitingChord = norm(previousChord) <= norm(nextChord)
                ? previousChord
                : nextChord;

        const tangentDirection = normalize(tangent);
        if (dot(tangentDirection, previousChord) <= 0 ||
            dot(tangentDirection, nextChord) <= 0)
        {
            const bisector = normalize(previousChord) + normalize(nextChord);
            tangent = norm(bisector) <= TOLERANCE.zeroLength
                    ? nextChord
                    : normalize(bisector) * norm(tangent);
        }
    }

    const maximumTangentLength = 1.5 * norm(limitingChord);
    if (norm(tangent) > maximumTangentLength)
    {
        tangent = normalize(tangent) * maximumTangentLength;
    }
    return tangent;
}

// Keep each control polygon monotonic along its chord so the cubic cannot fold backwards along it.
function constrainBezierControls(startPoint is Vector, firstControl is Vector,
    secondControl is Vector, endPoint is Vector) returns array
{
    const chord = endPoint - startPoint;
    const chordLength = norm(chord);
    const chordDirection = chord / chordLength;
    const maximumHandleLength = 0.75 * chordLength;

    var firstHandle = firstControl - startPoint;
    var secondHandle = endPoint - secondControl;
    var firstProjection = dot(firstHandle, chordDirection);
    var secondProjection = dot(secondHandle, chordDirection);

    if (firstProjection < 0)
    {
        firstHandle -= chordDirection * firstProjection;
    }
    if (secondProjection < 0)
    {
        secondHandle -= chordDirection * secondProjection;
    }

    if (norm(firstHandle) > maximumHandleLength)
    {
        firstHandle = normalize(firstHandle) * maximumHandleLength;
    }
    if (norm(secondHandle) > maximumHandleLength)
    {
        secondHandle = normalize(secondHandle) * maximumHandleLength;
    }

    firstProjection = dot(firstHandle, chordDirection);
    secondProjection = dot(secondHandle, chordDirection);
    if (firstProjection + secondProjection > chordLength)
    {
        const projectionScale =
                chordLength / (firstProjection + secondProjection);
        firstHandle *= projectionScale;
        secondHandle *= projectionScale;
    }

    return [startPoint + firstHandle, endPoint - secondHandle];
}

function directionInPlane(sketchPlane is Plane, direction is Vector) returns Vector
{
    const yDirection = cross(sketchPlane.normal, sketchPlane.x);
    return normalize(vector(dot(direction, sketchPlane.x), dot(direction, yDirection)));
}

function rotateInPlane(direction is Vector, angle is ValueWithUnits) returns Vector
{
    return vector(
        direction[0] * cos(angle) - direction[1] * sin(angle),
        direction[0] * sin(angle) + direction[1] * cos(angle)
    );
}

function rotateTowards(direction is Vector, targetDirection is Vector, angle is ValueWithUnits) returns Vector
{
    const crossValue =
            direction[0] * targetDirection[1] - direction[1] * targetDirection[0];
    return rotateInPlane(direction, crossValue >= 0 ? angle : -angle);
}

// Give larger active-guide amplitudes more backbone length; 0.5 approximates constant sine crest curvature.
function adaptiveLobeBoundaries(context is Context, backbone is Query, guide1 is Query, guide2 is Query,
    guide1Reversed is boolean, guide2Reversed is boolean, nodes is number,
    flipFirstCrest is boolean, amplitudeSpacing is number) returns array
{
    const lobeCount = nodes;
    var boundaries = [];
    for (var boundaryIndex = 0; boundaryIndex <= lobeCount; boundaryIndex += 1)
    {
        boundaries = append(boundaries, boundaryIndex / lobeCount);
    }

    for (var iteration = 0; iteration < 3; iteration += 1)
    {
        var centreParameters = [];
        for (var lobeIndex = 0; lobeIndex < lobeCount; lobeIndex += 1)
        {
            centreParameters = append(
                centreParameters,
                (boundaries[lobeIndex] + boundaries[lobeIndex + 1]) / 2
            );
        }

        const backboneCentres = evEdgeTangentLines(context, {
                    "edge" : backbone,
                    "parameters" : centreParameters,
                    "arcLengthParameterization" : true
                });
        const guide1Centres = evEdgeTangentLines(context, {
                    "edge" : guide1,
                    "parameters" : orientedParameters(centreParameters, guide1Reversed),
                    "arcLengthParameterization" : true
                });
        const guide2Centres = evEdgeTangentLines(context, {
                    "edge" : guide2,
                    "parameters" : orientedParameters(centreParameters, guide2Reversed),
                    "arcLengthParameterization" : true
                });

        var amplitudes = [];
        var totalAmplitude = 0 * meter;
        for (var lobeIndex = 0; lobeIndex < lobeCount; lobeIndex += 1)
        {
            const useGuide1 =
                    (lobeIndex % 2 == 0) == !flipFirstCrest;
            const guidePoint = useGuide1
                    ? guide1Centres[lobeIndex].origin
                    : guide2Centres[lobeIndex].origin;
            const amplitude = norm(guidePoint - backboneCentres[lobeIndex].origin);
            amplitudes = append(amplitudes, amplitude);
            totalAmplitude += amplitude;
        }

        const averageAmplitude = totalAmplitude / lobeCount;
        if (averageAmplitude <= TOLERANCE.zeroLength * meter)
        {
            return boundaries;
        }

        var weights = [];
        var totalWeight = 0;
        for (var amplitude in amplitudes)
        {
            const relativeAmplitude = max(amplitude / averageAmplitude, 0.05);
            const weight = relativeAmplitude ^ amplitudeSpacing;
            weights = append(weights, weight);
            totalWeight += weight;
        }

        var updatedBoundaries = [0];
        var cumulativeWeight = 0;
        for (var lobeIndex = 0; lobeIndex < lobeCount; lobeIndex += 1)
        {
            cumulativeWeight += weights[lobeIndex];
            updatedBoundaries = append(
                updatedBoundaries,
                lobeIndex == lobeCount - 1 ? 1 : cumulativeWeight / totalWeight
            );
        }
        boundaries = updatedBoundaries;
    }
    return boundaries;
}

function sampledParameters(boundaries is array, samplesPerLobe is number) returns array
{
    var parameters = [];
    for (var lobeIndex = 0; lobeIndex < size(boundaries) - 1; lobeIndex += 1)
    {
        const lobeStart = boundaries[lobeIndex];
        const lobeEnd = boundaries[lobeIndex + 1];
        for (var sampleIndex = 0; sampleIndex < samplesPerLobe; sampleIndex += 1)
        {
            parameters = append(
                parameters,
                lobeStart + (lobeEnd - lobeStart) * sampleIndex / samplesPerLobe
            );
        }
    }
    return append(parameters, 1);
}

annotation {
    "Feature Type Name" : "Compliant Spring Curve",
    "Feature Name Template" : "Compliant Spring Curve: #nodes nodes"
}
export const compliantSpringCurve = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation {
            "Name" : "Backbone",
            "Filter" : EntityType.EDGE && SketchObject.YES,
            "MaxNumberOfPicks" : 1
        }
        definition.backbone is Query;

        annotation {
            "Name" : "Guide 1",
            "Filter" : EntityType.EDGE && SketchObject.YES,
            "MaxNumberOfPicks" : 1
        }
        definition.guide1 is Query;

        annotation {
            "Name" : "Guide 2",
            "Filter" : EntityType.EDGE && SketchObject.YES,
            "MaxNumberOfPicks" : 1
        }
        definition.guide2 is Query;

        annotation {
            "Name" : "Nodes",
            "Description" : "Each node is one half-cycle; two nodes form one complete cycle."
        }
        isInteger(definition.nodes, SPRING_NODE_BOUNDS);

        annotation {
            "Name" : "Generator",
            "Description" : "Sine samples a sinusoid; Flowing S ranges from smooth transitions to loops; Zigzag uses straight flanks."
        }
        definition.generator is SpringCurveGenerator;

        if (definition.generator == SpringCurveGenerator.SINE)
        {
            annotation { "Group Name" : "Sine tuning", "Collapsed By Default" : false }
            {
                annotation {
                    "Name" : "Crest softness",
                    "Description" : "Extends crest and trough tangent handles while retaining fold prevention."
                }
                isReal(definition.crestSoftness, CREST_SOFTNESS_BOUNDS);
            }
        }
        else if (definition.generator == SpringCurveGenerator.FLOWING_S)
        {
            annotation { "Group Name" : "Flowing S tuning", "Collapsed By Default" : false }
            {
                annotation {
                    "Name" : "Loopiness",
                    "Description" : "Ranges from a bounded S-curve at 0 to strongly extended handles and fold-back at 5."
                }
                isReal(definition.loopiness, LOOPINESS_BOUNDS);
            }
        }

        annotation {
            "Name" : "Amplitude spacing",
            "Description" : "0 is uniform, 0.5 approximates constant crest curvature, 1 scales directly with amplitude, and values over 1 increasingly tighten lower-amplitude lobes."
        }
        isReal(definition.amplitudeSpacing, AMPLITUDE_SPACING_BOUNDS);

        annotation {
            "Name" : "Guide adherence",
            "Description" : "0 follows the backbone; 1 reaches the selected guide at each crest or trough."
        }
        isReal(definition.guideAdherence, GUIDE_ADHERENCE_BOUNDS);

        annotation { "Name" : "Flip first crest", "Default" : false }
        definition.flipFirstCrest is boolean;

        annotation {
            "Name" : "Force backbone crossings",
            "Default" : false,
            "Description" : "Adds every zero crossing as an explicit Bezier landmark with a shared tangent."
        }
        definition.forceBackboneCrossings is boolean;

        if (definition.generator != SpringCurveGenerator.ZIGZAG)
        {
            annotation { "Group Name" : "Endpoint incidence", "Collapsed By Default" : true }
            {
                annotation {
                    "Name" : "Start angle",
                    "Description" : "0 aims towards the active guide endpoint; positive values rotate towards the backbone."
                }
                isAngle(definition.startAngle, ENDPOINT_ANGLE_BOUNDS);

                annotation {
                    "Name" : "End angle",
                    "Description" : "0 aims towards the active guide endpoint; positive values rotate towards the backbone."
                }
                isAngle(definition.endAngle, ENDPOINT_ANGLE_BOUNDS);
            }
        }

        annotation { "Group Name" : "Advanced", "Collapsed By Default" : true }
        {
            annotation { "Name" : "Samples per cycle" }
            isInteger(definition.samplesPerCycle, SPRING_SAMPLES_PER_CYCLE_BOUNDS);
        }
    }
    {
        const backbone = requireSingleEdge(context, definition.backbone, "backbone");
        const guide1 = requireSingleEdge(context, definition.guide1, "guide1");
        const guide2 = requireSingleEdge(context, definition.guide2, "guide2");

        const selectedEdges = qUnion([backbone, guide1, guide2]);
        const commonPlane = try silent(evOwnerSketchPlane(context, {
                        "entity" : selectedEdges,
                        "checkAllEntities" : true
                    }));
        if (commonPlane == undefined)
        {
            throw regenError(
                "Backbone and guides must be coplanar sketch edges.",
                ["backbone", "guide1", "guide2"]
            );
        }

        const backboneEnds = requireOpenEdge(context, backbone, "backbone");
        const guide1Ends = requireOpenEdge(context, guide1, "guide1");
        const guide2Ends = requireOpenEdge(context, guide2, "guide2");
        const guide1Reversed = guideIsReversed(guide1Ends, backboneEnds);
        const guide2Reversed = guideIsReversed(guide2Ends, backboneEnds);

        if (definition.samplesPerCycle % 4 != 0)
        {
            throw regenError("Samples per cycle must be a multiple of four.", ["samplesPerCycle"]);
        }

        const sampleCount = definition.nodes * definition.samplesPerCycle / 2;
        const lobeBoundaries = adaptiveLobeBoundaries(
            context,
            backbone,
            guide1,
            guide2,
            guide1Reversed,
            guide2Reversed,
            definition.nodes,
            definition.flipFirstCrest,
            definition.amplitudeSpacing
        );
        const parameters = sampledParameters(
            lobeBoundaries,
            definition.samplesPerCycle / 2
        );

        const backboneSamples = evEdgeTangentLines(context, {
                    "edge" : backbone,
                    "parameters" : parameters,
                    "arcLengthParameterization" : true
                });
        const guide1Samples = evEdgeTangentLines(context, {
                    "edge" : guide1,
                    "parameters" : orientedParameters(
                        parameters,
                        guide1Reversed
                    ),
                    "arcLengthParameterization" : true
                });
        const guide2Samples = evEdgeTangentLines(context, {
                    "edge" : guide2,
                    "parameters" : orientedParameters(
                        parameters,
                        guide2Reversed
                    ),
                    "arcLengthParameterization" : true
                });

        const quarterCycle = definition.samplesPerCycle / 4;
        const halfCycle = definition.samplesPerCycle / 2;
        var wavePoints = [];

        for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex += 1)
        {
            const backbonePoint = backboneSamples[sampleIndex].origin;
            const cycleSample = sampleIndex % definition.samplesPerCycle;
            var waveValue;

            if (cycleSample == 0 || cycleSample == halfCycle)
            {
                waveValue = 0;
            }
            else if (cycleSample == quarterCycle)
            {
                waveValue = 1;
            }
            else if (cycleSample == 3 * quarterCycle)
            {
                waveValue = -1;
            }
            else
            {
                waveValue = sin(
                    2 * PI * sampleIndex / definition.samplesPerCycle * radian
                );
            }

            if (definition.flipFirstCrest)
            {
                waveValue = -waveValue;
            }

            const guidePoint = waveValue >= 0
                    ? guide1Samples[sampleIndex].origin
                    : guide2Samples[sampleIndex].origin;
            wavePoints = append(
                wavePoints,
                backbonePoint +
                    abs(waveValue) * definition.guideAdherence *
                        (guidePoint - backbonePoint)
            );
        }

        var sketchPoints = [];
        for (var sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex += 1)
        {
            sketchPoints = append(
                sketchPoints,
                worldToPlane(commonPlane, wavePoints[sampleIndex])
            );
        }

        const startPoint = sketchPoints[0];
        const endPoint = sketchPoints[sampleCount];
        const endpointChord = endPoint - startPoint;
        var startBackboneDirection =
                directionInPlane(commonPlane, backboneEnds[0].direction);
        if (dot(startBackboneDirection, endpointChord) < 0)
        {
            startBackboneDirection = -startBackboneDirection;
        }

        var endBackboneDirection =
                directionInPlane(commonPlane, backboneEnds[1].direction);
        if (dot(endBackboneDirection, -endpointChord) < 0)
        {
            endBackboneDirection = -endBackboneDirection;
        }

        const firstGuideEndpoint = definition.flipFirstCrest
                ? guide2Samples[0].origin
                : guide1Samples[0].origin;
        const lastLobeUsesGuide1 =
                (definition.nodes % 2 == 1) == !definition.flipFirstCrest;
        const lastGuideEndpoint = lastLobeUsesGuide1
                ? guide1Samples[sampleCount].origin
                : guide2Samples[sampleCount].origin;
        var startGuideDirection =
                worldToPlane(commonPlane, firstGuideEndpoint) - startPoint;
        if (norm(startGuideDirection) <= TOLERANCE.zeroLength * meter)
        {
            startGuideDirection = startBackboneDirection;
        }
        else
        {
            startGuideDirection = normalize(startGuideDirection);
        }

        var endGuideDirection =
                worldToPlane(commonPlane, lastGuideEndpoint) - endPoint;
        if (norm(endGuideDirection) <= TOLERANCE.zeroLength * meter)
        {
            endGuideDirection = endBackboneDirection;
        }
        else
        {
            endGuideDirection = normalize(endGuideDirection);
        }

        var startDirection = startGuideDirection;
        var endDirection = endGuideDirection;
        if (definition.generator != SpringCurveGenerator.ZIGZAG)
        {
            startDirection = rotateTowards(
                startGuideDirection,
                startBackboneDirection,
                definition.startAngle
            );
            endDirection = rotateTowards(
                endGuideDirection,
                endBackboneDirection,
                definition.endAngle
            );
        }

        const springSketch = newSketchOnPlane(context, id + "springSketch", {
                    "sketchPlane" : commonPlane
                });

        const boundaryIndices = segmentBoundaryIndices(
            definition.nodes,
            definition.samplesPerCycle,
            definition.forceBackboneCrossings
        );
        var landmarks = [];
        for (var boundaryIndex in boundaryIndices)
        {
            landmarks = append(landmarks, sketchPoints[boundaryIndex]);
        }

        const segmentCount = size(boundaryIndices) - 1;
        var firstControls = [];
        var secondControls = [];
        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex += 1)
        {
            const startIndex = boundaryIndices[segmentIndex];
            const endIndex = boundaryIndices[segmentIndex + 1];
            const segmentStart = landmarks[segmentIndex];
            const segmentEnd = landmarks[segmentIndex + 1];
            const chord = segmentEnd - segmentStart;
            var firstControl;
            var secondControl;

            if (definition.generator == SpringCurveGenerator.ZIGZAG)
            {
                firstControl = segmentStart + chord / 3;
                secondControl = segmentStart + 2 * chord / 3;
            }
            else if (definition.generator == SpringCurveGenerator.SINE)
            {
                var startTangent =
                        boundedTangentAtKnot(sketchPoints, startIndex, quarterCycle);
                var endTangent =
                        boundedTangentAtKnot(sketchPoints, endIndex, quarterCycle);
                const softnessScale = 1 + 1.5 * definition.crestSoftness;
                if (startIndex % halfCycle == quarterCycle)
                {
                    startTangent *= softnessScale;
                }
                if (endIndex % halfCycle == quarterCycle)
                {
                    endTangent *= softnessScale;
                }
                firstControl = segmentStart + startTangent / 3;
                secondControl = segmentEnd - endTangent / 3;
            }
            else
            {
                const scaledLoopiness = 0.3 * definition.loopiness;
                const handleRatio = 0.35 + 0.9 * scaledLoopiness;
                const startTangent =
                        landmarkTangent(landmarks, segmentIndex, handleRatio);
                const endTangent =
                        landmarkTangent(landmarks, segmentIndex + 1, handleRatio);
                firstControl = segmentStart + startTangent / 3;
                secondControl = segmentEnd - endTangent / 3;
            }

            var adjustedFirstControl = firstControl;
            var adjustedSecondControl = secondControl;
            if (definition.generator != SpringCurveGenerator.ZIGZAG && segmentIndex == 0)
            {
                adjustedFirstControl =
                        segmentStart + startDirection * norm(firstControl - segmentStart);
            }
            if (definition.generator != SpringCurveGenerator.ZIGZAG &&
                segmentIndex == segmentCount - 1)
            {
                adjustedSecondControl =
                        segmentEnd + endDirection * norm(secondControl - segmentEnd);
            }

            const constrainedControls = constrainBezierControls(
                segmentStart,
                adjustedFirstControl,
                adjustedSecondControl,
                segmentEnd
            );
            var outputControls = constrainedControls;
            if (definition.generator == SpringCurveGenerator.FLOWING_S)
            {
                const scaledLoopiness = 0.3 * definition.loopiness;
                outputControls = [
                    constrainedControls[0] +
                        scaledLoopiness *
                            (adjustedFirstControl - constrainedControls[0]),
                    constrainedControls[1] +
                        scaledLoopiness *
                            (adjustedSecondControl - constrainedControls[1])
                ];
            }
            firstControls = append(firstControls, outputControls[0]);
            secondControls = append(secondControls, outputControls[1]);
        }

        if (definition.forceBackboneCrossings &&
            definition.generator != SpringCurveGenerator.ZIGZAG)
        {
            for (var landmarkIndex = 1;
                landmarkIndex < size(landmarks) - 1;
                landmarkIndex += 1)
            {
                const sampleIndex = boundaryIndices[landmarkIndex];
                if (sampleIndex % halfCycle != 0)
                {
                    continue;
                }

                var crossingTangent;
                if (definition.generator == SpringCurveGenerator.SINE)
                {
                    crossingTangent =
                            boundedTangentAtKnot(sketchPoints, sampleIndex, quarterCycle);
                }
                else
                {
                    const scaledLoopiness = 0.3 * definition.loopiness;
                    const handleRatio = 0.35 + 0.9 * scaledLoopiness;
                    crossingTangent =
                            landmarkTangent(landmarks, landmarkIndex, handleRatio);
                }

                const crossingPoint = landmarks[landmarkIndex];
                const incomingHandle =
                        crossingPoint - secondControls[landmarkIndex - 1];
                const outgoingHandle =
                        firstControls[landmarkIndex] - crossingPoint;
                const sharedHandle =
                        normalize(crossingTangent) *
                            min(norm(incomingHandle), norm(outgoingHandle));
                secondControls[landmarkIndex - 1] =
                        crossingPoint - sharedHandle;
                firstControls[landmarkIndex] =
                        crossingPoint + sharedHandle;
            }
        }

        for (var segmentIndex = 0; segmentIndex < segmentCount; segmentIndex += 1)
        {
            skBezier(springSketch, "wave" ~ segmentIndex, {
                        "points" : [
                            landmarks[segmentIndex],
                            firstControls[segmentIndex],
                            secondControls[segmentIndex],
                            landmarks[segmentIndex + 1]
                        ]
                    });
        }
        skSolve(springSketch);
    });
