# MVP-E12-S37 — Serialize Local Candidate Packaging

## Outcome

One local command produces an immutable, version-scoped `dnaxi` candidate
package directory without allowing overlapping pack operations.

## Design

- [Platform and packaging](../../design/runtime-and-distribution.md#platform-and-packaging)

## Boundary

This is a local development workflow. It does not publish a package, install a
persistent tool, or change release automation.

## Acceptance

- Candidate packing uses an exclusive persistent lock file and never unlinks
  a lock another process may own.
- Every version is packed into its own new directory and an existing version
  is never overwritten.
- The version directory contains exactly one `.nupkg` and one `.snupkg`, so
  the existing package verifier remains reusable across multiple candidates.
- Packing consumes the canonical restored state and the resulting exact
  version runs through `dnx dnaxi@<version> --source <version-directory>`.

## Verification

- Tests cover lock contention, immutable version directories, and
  verifier-compatible layout.
- A fresh local candidate passes package verification and an exact `dnx dnaxi`
  smoke test.
