# MVP-E12-S17 — Publish and Verify 0.2.0

## Outcome

The verified `dotnet-axi` `0.2.0` package and matching GitHub Release are
publicly available and installable through both persistent tool and `dnx`
invocation.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

This is the release action. It must remain blocked until the user explicitly
authorizes publication after approving the completed release candidate.

## Acceptance

- The candidate pull request is merged without unrelated changes, and its
  exact release commit receives the immutable `v0.2.0` tag.
- Protected publication completes with the approved identity and produces the
  matching NuGet package, symbols, GitHub Release, checksums, and evidence.
- Fresh public-source global, local, and `dnx` invocations report `0.2.0` and
  exercise the documented workspace-aware capability.
- The `0.2.0` milestone closes only after public verification succeeds.

## Verification

- NuGet ownership, indexing, package metadata, symbols, install, update,
  uninstall, and one-shot execution are verified from clean temporary stores.
- GitHub tag, release, package links, commit, and checksums agree.

## Dependencies

- `MVP-E12-S16`
