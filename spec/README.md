# Pure Python SPEC — separate research artifact

This is NOT the NT8/C# copier engine. The exact research kernel SHA256 is `f9974ad29b5ef6ba7619434a47d0b97597fc9b15f9a5c8e9c7a589176875a024`.

Published synthetic suites: reviewed388 safety assertions, minimal4 witnesses and corrected independent40 cases. The independent40 initial harness previously reported16P24F: tuple/list comparison and three STOP admission fixtures were corrected in a separately retained local run. This public suite contains the corrected inputs, not a claim the original run passed.

Bounded contract:
- Submit/Change ACK total matches attempted quantity and cannot be below retained captured/applied positive fills.
- Cancel ACK reports the pre-cancel acknowledged historical total; it cannot create Submit/Change capacity.
- Unaccepted Register price hints cannot become acknowledged stop prices; explicit matching price-bearing ACK may establish that channel.
- Seed is a trusted synthetic checkpoint, not provider authentication. Existing unknown-price Seed policy is retained, not global stop/Flatten soundness.

Other historical suites: execution-time91 still has89 met expectations and two unresolved session/generation hypotheses; historical alias3 inconsistencies remain. They are not silently made GREEN and are not included as passing portable tests. No engineering/provider acceptance follows from these tests.
