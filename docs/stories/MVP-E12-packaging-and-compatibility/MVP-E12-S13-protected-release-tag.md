# MVP-E12-S13 — Protect Release-tag Creation

## Outcome

An explicitly approved workflow can create one validated release tag on an
exact commit from `main`.

## Design

- [Release procedure](../../design/releases.md#release-procedure)

## Boundary

The workflow supports a no-write dry run. Creating a real tag remains a
separate release action and is not authorized by implementing this story.

## Acceptance

- Inputs identify an exact version and commit; the workflow derives the tag as
  `v<version>` rather than accepting an arbitrary ref name.
- It refuses commits outside `main`, non-SemVer versions, existing or
  conflicting tags, and candidates that fail release verification.
- Only the tag-creation job receives `contents: write`, and that job is gated
  by the protected release environment.
- Repeated or concurrent requests cannot move or replace a release tag.

## Verification

- Dry-run cases cover valid input and every refusal condition without creating
  a ref.
- Workflow permissions and concurrency are inspected as enforceable release
  controls.

## Dependencies

- `MVP-E12-S11`
- `MVP-E12-S12`
