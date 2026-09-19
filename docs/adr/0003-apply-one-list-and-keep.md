# Apply takes one list; keep is an option

Apply receives one already-built Tap or Die plus one Location list (Matching or Mating) and iterates. Each row instantiates Opposite, End, Clock, Finish, and envelope. Matching versus Mating is not a per-row flag. Keep-tools is an option on that apply: the boolean consumes copies; a kept Thread tap, Thread die, Through tap, or Through die is another instantiation of the same definition. Hidden part-pick fields (`boreParts`, `boreMatingPart`, and the unused pin-stock helpers) are dead.
