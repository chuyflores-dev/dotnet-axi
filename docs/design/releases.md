# Releases and Versioning

This document defines product-version meaning, release milestones, and the
boundary between continuous verification and external package publication.

## Version authorities

`dotnet-axi` follows Semantic Versioning for the NuGet package and installed
tool:

- A release tag named `v<package-version>` is the release-version authority and
  identifies the exact release commit.
- The repository-pinned MinVer CLI calculates the version once for CI and
  release builds. That value is passed explicitly to MSBuild for the package,
  assemblies, and embedded `dnaxi` version. CI checks out complete Git history
  so tag discovery is deterministic.
- The GitHub Release and NuGet package use that same version.
- Output-schema versions are independent. A pre-1.0 tool release does not
  permit an incompatible change to published schema `dotnet-axi/v1`; schema
  evolution follows the output-contract rules.

Untagged CI builds use MinVer's height-bearing prerelease version in the
current planned minor line. After a stable tag, ordinary `main` CI builds
advance to the next minor `alpha.0` line. Local builds use a fixed
height-free `alpha.0.local` fallback so sandboxed agents do not need Git
metadata access merely to compile or test. All such artifacts are disposable,
are associated with their Git commit by the verification environment, and
must not be published. An exact version override is permitted only for
non-publishing candidate verification; it never becomes a second version
authority.

## Planned capability milestones

Milestones are cumulative release gates. They identify the capability that
must be coherent by that version; they do not postpone applicable security,
compatibility, or quality work until a later milestone.

| Version | Release outcome | Primary delivery gate |
|---|---|---|
| `0.1.0` | First installable agent-facing CLI contract | CLI/output foundation, bounded structured help and version output, query-planning seams, and verified .NET tool invocation |
| `0.2.0` | Workspace-aware CLI | Workspace selection, worktree state, project/framework coverage, snapshot identity, the passive home view, and an installable on-demand Agent Skill |
| `0.3.0` | First-use source discovery | File, literal, regular-expression, structural, and stable Roslyn syntax searches without a mandatory index, plus the first measured Codex agent-task comparison |
| `0.4.0` | Stable source identity and bounded context | Symbol discovery, stateless entity IDs, candidate verification, show, outline, and context budgets |
| `0.5.0` | Semantic relationships and graphs | References, implementations, inheritance, callers/callees, project graphs, paths, cycles, and impact |
| `0.6.0` | Analysis and structured SDK execution | Compiler/configured analysis plus noninteractive restore, build, test, format, and constrained `dotnet` execution |
| `0.7.0` | Configurable validation | Repository configuration, freshness, affected scope, and deterministic fast and standard validation profiles |
| `0.8.0` | Safe agent integration | Claude Code and Codex setup, repair, removal, effect disclosure, process safety, secret protection, diagnostic artifacts, and the Claude benchmark adapter |
| `0.9.0` | Feature-complete MVP preview | Packaging and compatibility matrices plus release-level correctness, security, performance, and separate Codex and Claude agent-task evidence |
| `1.0.0` | Supported MVP release | The complete requirements release bar passes with no known release blocker; only stabilization changes are expected after `0.9.0` |

The epic containing a story and the version targeting that story are separate
dimensions. Cross-cutting E11–E13 stories should land with the earlier
capability they protect even though their full epic completion gates `0.8.0`
or `0.9.0`.

## Version increments

- `0.x.0` adds the next cumulative capability milestone.
- `0.x.y` is reserved for compatible defect, security, packaging, or
  documentation corrections to an already released `0.x` line.
- `1.x.0` may add compatible post-MVP capability after `1.0.0`.
- A breaking installed-CLI contract after `1.0.0` requires the next major
  product version. Output-schema compatibility is evaluated separately.

Before a stable milestone, deliberate prereleases may use:

- `alpha.N` while capability and contract work is incomplete;
- `beta.N` after the milestone capability is complete but still stabilizing;
- `rc.N` when only release blockers may change.

Not every milestone must publish every prerelease phase. Until the first
stable tag, the configured minimum minor keeps local builds in the
`0.2.0-alpha.0` line. Publishing any untagged build is outside the release
process. Feature and post-release pull requests do not perform release-version
bumps.

## Verification and publishing boundary

Pull-request and `main` CI may restore, build, test, pack, inspect, and install
a package in an isolated temporary store. Those jobs:

- never publish to NuGet;
- never create a tag or GitHub Release;
- never receive the NuGet publishing credential; and
- do not retain a package as an official release artifact.

External publication begins when an authorized maintainer publishes a GitHub
Release for `v<version>` at an exact commit from `main`. The release creates
the tag when it does not already exist and triggers the release workflow. The
workflow refuses to publish unless:

- the tag identifies a commit on `main`;
- tag, package, embedded tool version, and requested release version match;
- canonical tests and package install/invocation checks pass from a clean
  checkout;
- package identity, command name, license, symbols, and compatibility evidence
  satisfy the target milestone; and
- that package version has not already been published.

The release workflow delegates verification to the reusable release-candidate
workflow instead of rebuilding a second publication evidence system. That
workflow remains manually runnable as a non-publishing rehearsal. It produces
the exact package and symbol package after canonical tests, package inspection,
and global, local, and `dnx` checks on Linux, macOS, and Windows.

Only the protected publication job receives the NuGet credential, and only its
final push step can use it. The job downloads the exact verified candidate and
publishes its `.nupkg`; NuGet publishes the adjacent symbol package. Duplicate
versions fail rather than being skipped. The NuGet package is the distributed
tool, so the workflow does not copy package or evidence files onto the already
published GitHub Release.

The `release` environment requires approval and admits only `v*` tag refs.
NuGet trusted publishing binds its short-lived credential to this repository,
`release.yml`, that environment, and the selected NuGet owner. A scoped,
expiring API key is a fallback only when that owner cannot use trusted
publishing. Such a key must be limited to the `dotnet-axi` package, stored only
in the protected environment, introduced through a reviewed workflow change,
and revoked with its environment secret immediately after publication.
Published GitHub Releases are immutable so their version tags cannot later
move.

## Release procedure

1. Complete the target capability, publication, and trusted-publishing issues.
2. Prepare and review a release-candidate pull request with final release notes
   and user-facing examples, then run the non-publishing candidate workflow.
3. After explicit release authorization, merge that pull request and record its
   exact commit.
4. Publish the release and create its tag with
   `gh release create v<version> --target <exact-main-sha> --generate-notes`.
5. Approve the protected `release` environment and let the release workflow
   publish the verified package and symbols.
6. Verify global, local, and `dnx` invocation from the public NuGet source.
7. Confirm untagged `main` builds resolve to the next planned prerelease line,
   then close the publication issue and milestone.

GitHub milestones use the stable target names `0.1.0` through `1.0.0`.
Prerelease issues remain assigned to their eventual stable milestone. Project
status tracks execution state independently. Milestones have no due date until
the project adopts an actual release schedule.
