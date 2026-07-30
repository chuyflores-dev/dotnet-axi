# MVP-E13-S08 — Generate the Large-repository Fixture

## Outcome

A committed generator and fixed manifest reproducibly create the approximately
50,000-file performance repository.

## Design

- [Performance benchmark](../../design/quality.md#performance-benchmark)

## Boundary

Generated repository content is derived test data; the committed generator,
manifest, and seed are the maintained source.

## Acceptance

- The fixture exercises catalog, text, structural, candidate semantic, project
  dependency, and changed-scope work at documented scale.
- Generation records a stable fixture hash and rejects unexpected drift.

## Verification

- Repeated generation from a clean state produces the same manifest, topology,
  representative content hashes, and file/project counts.

## Dependencies

- `MVP-E13-S01`
