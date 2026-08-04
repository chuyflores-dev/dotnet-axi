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

External publication requires an explicit release action. The future release
workflow must be manually dispatched for an existing `v<version>` tag and use
a protected GitHub environment that requires approval. It must refuse to
publish unless:

- the tag identifies a commit on `main`;
- tag, package, embedded tool version, and requested release version match;
- canonical tests and package install/invocation checks pass from a clean
  checkout;
- package identity, command name, license, symbols, and compatibility evidence
  satisfy the target milestone; and
- that package version has not already been published.

Only the release workflow receives the NuGet credential. It publishes the
`.nupkg` and symbol package, attaches verified artifacts and evidence to the
GitHub Release, and reports the resulting package URL.

## Release procedure

1. Complete the target capability and readiness issues, leaving the explicit
   publication issue open.
2. Merge a release PR containing final release notes and user-facing examples.
3. Run the protected tag action for that commit as `v<version>`.
4. Manually approve and run the protected release workflow for the tag.
5. Verify installation and `dnx` execution from the public NuGet source.
6. Publish the GitHub Release with its compatibility and quality evidence.
7. Confirm untagged `main` builds resolve to the next planned prerelease line,
   then close the publication issue and milestone.

GitHub milestones use the stable target names `0.1.0` through `1.0.0`.
Prerelease issues remain assigned to their eventual stable milestone. Project
status tracks execution state independently. Milestones have no due date until
the project adopts an actual release schedule.
