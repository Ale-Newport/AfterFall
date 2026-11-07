# ADR-0004: Versioned JSON saves with checksum, migration and backup

- **Status:** accepted (2026-11-07)
- **Decision:** Saves are JSON envelopes `{format, schema, checksum, payload}` built from explicit DTOs (`WorldStateMapper`), never Unity scene serialization. The SHA-256 checksum covers the canonical payload. Older schemas run through registered `SaveMigrations`; newer schemas are rejected. Writes go to a temp file followed by `File.Replace`, which keeps a `.bak`. A load that fails validation falls back to the backup. Static terrain is regenerated from the seed.
- **Consequences:** Human-readable saves with corruption detection and recovery. Every new piece of persistent state must be added to the DTOs and the mapper, together with a round-trip test.
