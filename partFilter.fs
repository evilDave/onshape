FeatureScript 3070;
import(path : "onshape/std/common.fs", version : "3070.0");
import(path : "onshape/std/queryVariable.fs", version : "3070.0");

// Part Filter selects a reusable subset of solid bodies by bounding-box size or
// spatial relationship to reference geometry. It tests each input body independently,
// then can publish the matches as a robust query variable and/or delete non-matches.
//
// Bounding-box dimensions are explicit (direction, ranked dimension, any, or every)
// to avoid ambiguous multiselect behaviour. Intersects includes touching, overlap,
// and containment, while containment modes are strict and require solid references.
// Empty inputs and results are valid; destructive operations are skipped when empty.

export enum PartFilterQueryType
{
    annotation { "Name" : "Bounding box length" }
    BOUNDING_BOX_LENGTH,

    annotation { "Name" : "Spatial relation" }
    SPATIAL_RELATION
}

export enum PartFilterBoundingBoxDimension
{
    annotation { "Name" : "Along direction" }
    DIRECTION,

    annotation { "Name" : "Longest" }
    LONGEST,

    annotation { "Name" : "Middle" }
    MIDDLE,

    annotation { "Name" : "Shortest" }
    SHORTEST,

    annotation { "Name" : "Any" }
    ANY,

    annotation { "Name" : "Every" }
    EVERY
}

export enum PartFilterComparison
{
    annotation { "Name" : "At most" }
    AT_MOST,

    annotation { "Name" : "At least" }
    AT_LEAST
}

export enum PartFilterSpatialRelation
{
    annotation { "Name" : "Disjoint" }
    DISJOINT,

    annotation { "Name" : "Intersects" }
    INTERSECTS,

    annotation { "Name" : "Contained by" }
    CONTAINED_BY,

    annotation { "Name" : "Contains" }
    CONTAINS
}

function lengthMatches(length is ValueWithUnits, definition is map) returns boolean
{
    return definition.comparison == PartFilterComparison.AT_MOST
            ? length <= definition.lengthThreshold
            : length >= definition.lengthThreshold;
}

function boundingBoxMatches(context is Context, part is Query, definition is map, direction) returns boolean
{
    if (definition.boundingBoxDimension == PartFilterBoundingBoxDimension.DIRECTION)
    {
        const measurementSystem = coordSystem(
            vector(0, 0, 0) * meter,
            perpendicularVector(direction),
            direction
        );
        const bounds = evBox3d(context, {
                    "topology" : part,
                    "cSys" : measurementSystem,
                    "tight" : true
                });
        return lengthMatches(bounds.maxCorner[2] - bounds.minCorner[2], definition);
    }

    const bounds = evBox3d(context, {
                "topology" : part,
                "tight" : true
            });
    const dimensions = [
        bounds.maxCorner[0] - bounds.minCorner[0],
        bounds.maxCorner[1] - bounds.minCorner[1],
        bounds.maxCorner[2] - bounds.minCorner[2]
    ];

    if (definition.boundingBoxDimension == PartFilterBoundingBoxDimension.ANY)
    {
        for (var dimension in dimensions)
        {
            if (lengthMatches(dimension, definition))
            {
                return true;
            }
        }
        return false;
    }

    if (definition.boundingBoxDimension == PartFilterBoundingBoxDimension.EVERY)
    {
        for (var dimension in dimensions)
        {
            if (!lengthMatches(dimension, definition))
            {
                return false;
            }
        }
        return true;
    }

    const shortest = min(dimensions);
    const longest = max(dimensions);
    const selectedDimension =
            definition.boundingBoxDimension == PartFilterBoundingBoxDimension.LONGEST
            ? longest
            : definition.boundingBoxDimension == PartFilterBoundingBoxDimension.MIDDLE
                    ? dimensions[0] + dimensions[1] + dimensions[2] - shortest - longest
                    : shortest;
    return lengthMatches(selectedDimension, definition);
}

function spatialRelationMatches(context is Context, part is Query, definition is map) returns boolean
{
    const collisions = evCollision(context, {
                "tools" : part,
                "targets" : definition.spatialReference
            });

    var intersects = false;
    for (var collision in collisions)
    {
        const clashType = collision["type"];
        if (clashType == ClashType.NONE)
        {
            continue;
        }

        intersects = true;
        if (definition.spatialRelation == PartFilterSpatialRelation.CONTAINED_BY &&
            clashType == ClashType.TOOL_IN_TARGET)
        {
            return true;
        }
        if (definition.spatialRelation == PartFilterSpatialRelation.CONTAINS &&
            clashType == ClashType.TARGET_IN_TOOL)
        {
            return true;
        }
    }

    if (definition.spatialRelation == PartFilterSpatialRelation.DISJOINT)
    {
        return !intersects;
    }
    if (definition.spatialRelation == PartFilterSpatialRelation.INTERSECTS)
    {
        return intersects;
    }
    return false;
}

annotation {
    "Feature Type Name" : "Part Filter",
    "Feature Name Template" : "#name",
    "UIHint" : UIHint.NO_PREVIEW_PROVIDED
}
export const partFilter = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Parts", "Filter" : EntityType.BODY && BodyType.SOLID }
        definition.parts is Query;

        annotation { "Name" : "Query type" }
        definition.queryType is PartFilterQueryType;

        if (definition.queryType == PartFilterQueryType.BOUNDING_BOX_LENGTH)
        {
            annotation { "Name" : "Dimension" }
            definition.boundingBoxDimension is PartFilterBoundingBoxDimension;

            if (definition.boundingBoxDimension == PartFilterBoundingBoxDimension.DIRECTION)
            {
                annotation {
                    "Name" : "Direction",
                    "Filter" : QueryFilterCompound.ALLOWS_DIRECTION || BodyType.MATE_CONNECTOR,
                    "MaxNumberOfPicks" : 1
                }
                definition.direction is Query;
            }

            annotation { "Name" : "Comparison" }
            definition.comparison is PartFilterComparison;

            annotation { "Name" : "Length threshold" }
            isLength(definition.lengthThreshold, NONNEGATIVE_LENGTH_BOUNDS);
        }
        else if (definition.queryType == PartFilterQueryType.SPATIAL_RELATION)
        {
            annotation { "Name" : "Relation" }
            definition.spatialRelation is PartFilterSpatialRelation;

            annotation {
                "Name" : "Reference",
                "Filter" : EntityType.BODY || EntityType.FACE || EntityType.EDGE || EntityType.VERTEX
            }
            definition.spatialReference is Query;
        }

        annotation { "Group Name" : "Outputs", "Collapsed By Default" : false }
        {
            annotation { "Name" : "Show selection", "Default" : true }
            definition.showSelection is boolean;

            annotation { "Name" : "Create or update query variable", "Default" : true }
            definition.createQueryVariable is boolean;

            if (definition.createQueryVariable)
            {
                annotation {
                    "Name" : "Variable name",
                    "Default" : "filteredParts",
                    "MaxLength" : 10000,
                    "UIHint" : [UIHint.UNCONFIGURABLE, UIHint.QUERY_VARIABLE_NAME]
                }
                definition.variableName is string;
            }

            annotation { "Name" : "Delete non-matching bodies", "Default" : false }
            definition.deleteNonMatchingBodies is boolean;
        }
    }
    {
        var featureName = definition.createQueryVariable
                ? definition.variableName
                : "Part Filter";
        if (definition.deleteNonMatchingBodies)
        {
            featureName ~= " (delete)";
        }
        setFeatureComputedParameter(context, id, {
                    "name" : "name",
                    "value" : featureName
                });

        var direction;
        if (definition.queryType == PartFilterQueryType.BOUNDING_BOX_LENGTH &&
            definition.boundingBoxDimension == PartFilterBoundingBoxDimension.DIRECTION)
        {
            verifyNonemptyQuery(context, definition, "direction", "Select a direction.");
            direction = extractDirection(context, definition.direction);
            if (direction == undefined)
            {
                throw regenError("Select a valid direction.", ["direction"]);
            }
        }
        else if (definition.queryType == PartFilterQueryType.SPATIAL_RELATION)
        {
            verifyNonemptyQuery(context, definition, "spatialReference", "Select reference geometry.");
            if (definition.spatialRelation == PartFilterSpatialRelation.CONTAINED_BY ||
                definition.spatialRelation == PartFilterSpatialRelation.CONTAINS)
            {
                const references = evaluateQuery(context, definition.spatialReference);
                const solidReferences = evaluateQuery(
                    context,
                    qBodyType(
                        qEntityFilter(definition.spatialReference, EntityType.BODY),
                        BodyType.SOLID
                    )
                );
                if (size(references) != size(solidReferences))
                {
                    throw regenError(
                        "Contained by and contains require solid part references.",
                        ["spatialReference"]
                    );
                }
            }
        }

        var matchingParts = [];
        var nonMatchingParts = [];

        for (var part in evaluateQuery(context, definition.parts))
        {
            const matches = definition.queryType == PartFilterQueryType.BOUNDING_BOX_LENGTH
                    ? boundingBoxMatches(context, part, definition, direction)
                    : spatialRelationMatches(context, part, definition);

            if (matches)
            {
                matchingParts = append(matchingParts, part);
            }
            else
            {
                nonMatchingParts = append(nonMatchingParts, part);
            }
        }

        var result = qNothing();
        if (size(matchingParts) > 0)
        {
            result = qUnion(makeRobustQueriesBatched(context, qUnion(matchingParts)));
        }

        if (definition.createQueryVariable)
        {
            setQueryVariable(context, definition.variableName, "Parts matched by Part Filter", result);
        }

        if (definition.showSelection)
        {
            try silent
            {
                addDebugEntities(context, result, DebugColor.YELLOW);
            }
        }
        setHighlightedEntities(context, { "entities" : result });

        if (definition.deleteNonMatchingBodies && size(nonMatchingParts) > 0)
        {
            const nonMatchingResult =
                    qUnion(makeRobustQueriesBatched(context, qUnion(nonMatchingParts)));
            opDeleteBodies(context, id + "deleteNonMatchingBodies", {
                        "entities" : nonMatchingResult
                    });
        }
    });
