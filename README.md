# dotnet-axi

An agent-first command-line interface for understanding, analyzing, validating,
and safely modifying .NET codebases.

The repository and product are named `dotnet-axi`. Starting with 0.4.0, the
.NET tool package and installed command are named `dnaxi`. The already
published 0.2.0 and 0.3.0 package ID remains `dotnet-axi`.

See
[REQUIREMENTS.md](https://github.com/chuyflores-dev/dotnet-axi/blob/main/REQUIREMENTS.md)
for the product requirements.
Technical behavior and architecture are documented in the
[reference index](https://github.com/chuyflores-dev/dotnet-axi/blob/main/docs/README.md).
Durable delivery scope lives in
[stories and epics](https://github.com/chuyflores-dev/dotnet-axi/blob/main/docs/stories/README.md);
GitHub Issues track live status.

## Run 0.4.0

`dnaxi` 0.4.0 requires the .NET 10 SDK. The primary path is an exact-version
one-shot invocation with no persistent tool installation:

```bash
dnx dnaxi@0.4.0 --verbosity quiet -- --version
```

Version 0.4.0 moves the package ID from `dotnet-axi` to `dnaxi` while keeping
the installed command `dnaxi`. The immutable 0.2.0 and 0.3.0 packages remain
under the old ID. See the
[0.4.0 release notes](https://github.com/chuyflores-dev/dotnet-axi/blob/main/docs/releases/0.4.0.md)
for the exact surface, measured Codex evidence, and known limitations.

## Source discovery

Search normalized paths or literal source text without a persistent index:

```bash
dnx dnaxi@0.4.0 --verbosity quiet -- search file 'Handler.cs' --path . --limit 20
dnx dnaxi@0.4.0 --verbosity quiet -- search text 'Archive pipeline ready.' --path . --limit 20
```

Use .NET regular-expression semantics explicitly, or request a stable syntax
shape backed by Roslyn:

```bash
dnx dnaxi@0.4.0 --verbosity quiet -- search text 'Handle(?:Audit|Retry)Async' --regex --path . --limit 20
dnx dnaxi@0.4.0 --verbosity quiet -- search syntax invocation --name Record --path . --limit 20
```

Syntax results are candidates for the requested shape, not compiler-verified
symbol identity. Use the returned retrieval command only when the bounded
response omits rows that are needed.

Global and repository-local tool installation remain compatibility paths:

```bash
dotnet tool install --global dnaxi --version 0.4.0
dnaxi --version

# Run this first only when the repository has no tool manifest.
dotnet new tool-manifest
dotnet tool install dnaxi --version 0.4.0
dotnet tool run dnaxi -- --version
```

## Development

The repository uses the .NET SDK selected by `global.json`.

```bash
dotnet restore dotnet-axi.slnx
dotnet build dotnet-axi.slnx --configuration Release --no-restore
dotnet test dotnet-axi.slnx --configuration Release --no-build
```

## Local package

Install the repository Agent Skill independently from the .NET tool package:

```bash
npx skills add chuyflores-dev/dotnet-axi --skill dotnet-axi -g
```

The skill teaches supported agents to invoke the exact `dnaxi` release through
`dnx`; installing the skill does not install the .NET tool persistently.

Create and verify a disposable local package without publishing it:

```bash
dotnet pack src/DotNetAxi.Cli/DotNetAxi.Cli.csproj \
  --configuration Release \
  --output artifacts/packages \
  -p:DotNetAxiBuildVersion=0.4.0-alpha.1

pwsh ./eng/verify-tool-package.ps1 \
  -PackageDirectory artifacts/packages

# Pack and run exact stable and prerelease versions through dnx only.
pwsh ./eng/verify-dnx-version-matrix.ps1
```

With .NET 10 or later, the local package can be invoked once without a
persistent installation:

```bash
dnx dnaxi@0.4.0-alpha.1 \
  --source ./artifacts/packages \
  --verbosity quiet \
  -- --version
```

This package-ID change applies only to 0.4.0 and later candidates and
releases. Continue to use `dotnet-axi@0.3.0` when invoking the immutable 0.3.0
package.

## License

Licensed under the
[Apache License 2.0](https://github.com/chuyflores-dev/dotnet-axi/blob/main/LICENSE).
