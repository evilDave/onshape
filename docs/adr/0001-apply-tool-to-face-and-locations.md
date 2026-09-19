# Apply one Tap or Die to the Thread face and to Locations

The Primary thread is the matching Tap or Die applied to the Thread face, not a separate helical-groove subtract. Locations use the same apply path: the module receives the Location list and its options (Opposite, End, Clock, Finish) plus the tool, and performs the geometry. Thread clearance belongs to the Mating tool, not to the caller or a Location. Stop at is Primary-only input to that same apply. One builder keeps the overlapping thread from being defined twice.
