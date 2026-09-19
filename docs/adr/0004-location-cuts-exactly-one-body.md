# Location cuts exactly one body

A Location subtract is only the owner of that Location. The candidate set is every solid at that Location (mate owner, owner body, and closest-to-origin), not the first query that hits. If that set has more than one solid, drop the Primary thread part and cut the remaining body when that leaves exactly one; otherwise warn and skip. Guessing among two unrelated parts, or passing both to the boolean, is rejected because an enclosed part that shares the mate face is still a different part.
