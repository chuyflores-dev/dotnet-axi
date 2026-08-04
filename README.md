# dotnet-axi

An agent-first command-line interface for understanding, analyzing, validating,
and safely modifying .NET codebases.

The repository and .NET tool package are named `dotnet-axi`. The installed
command is `dnaxi`.

See [REQUIREMENTS.md](REQUIREMENTS.md) for the product requirements.
Technical behavior and architecture are documented in the
[reference index](docs/README.md). Durable delivery scope lives in
[stories and epics](docs/stories/README.md); GitHub Issues track live status.

## Install 0.2.0

`dotnet-axi` 0.2.0 requires the .NET 10 SDK. Install it as a global tool:

```bash
dotnet tool install --global dotnet-axi --version 0.2.0
dnaxi --version
```

Or pin it in a repository-local tool manifest:

```bash
# Run this first only when the repository has no tool manifest.
dotnet new tool-manifest
dotnet tool install dotnet-axi --version 0.2.0
dotnet tool run dnaxi -- --version
```

With .NET 10 or later, run it once without a persistent installation:

```bash
dnx dotnet-axi@0.2.0 --verbosity quiet -- --version
```

Version 0.2.0 exposes the passive workspace home view (`dnaxi`), structured
help (`dnaxi --help`), and structured version output (`dnaxi --version`). It
does not expose capability subcommands yet; treat the installed version's help
as authoritative. See the [0.2.0 release notes](docs/releases/0.2.0.md) for the
included foundations and known limitations.

## Development

The repository uses the .NET SDK selected by `global.json`.

```bash
dotnet restore dotnet-axi.slnx
dotnet build dotnet-axi.slnx --configuration Release --no-restore
dotnet test dotnet-axi.slnx --configuration Release --no-build
```

## Local package

Create and verify a disposable local package without publishing it:

```bash
dotnet pack src/DotNetAxi.Cli/DotNetAxi.Cli.csproj \
  --configuration Release \
  --output artifacts/packages \
  -p:DotNetAxiBuildVersion=0.2.0

pwsh ./eng/verify-tool-package.ps1 \
  -PackageDirectory artifacts/packages
```

With .NET 10 or later, the local package can be invoked once without a
persistent installation:

```bash
dnx dotnet-axi@0.2.0 \
  --source ./artifacts/packages \
  --verbosity quiet \
  -- --version
```

## License

Licensed under the [Apache License 2.0](LICENSE).
