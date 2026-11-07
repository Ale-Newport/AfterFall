# ADR-0007: Electricity as a priority-shedding graph

- **Status:** accepted (2026-11-07)
- **Decision:** Networks are connected components of cables over complete electrical structures, rebuilt only when `PowerTopologyVersion` changes. Each tick, consumers (machines that are ready or working, lamps at night) are sorted by priority and either fully powered or shed with a stated reason. There are no partial brownouts, because that is easier to understand and explain. Generators burn fuel in proportion to delivered energy (an idle generator burns nothing). Batteries absorb surplus and cover deficits within rate limits.
- **Consequences:** Behaviour is predictable and testable. Future work: switches, cable damage from storms, circuit breakers, and distance losses if gameplay needs them.
