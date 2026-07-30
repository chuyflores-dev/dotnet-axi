# dotnet-axi

An agent-first command-line interface for understanding, analyzing, validating,
and safely modifying .NET codebases.

The repository and .NET tool package are named `dotnet-axi`. The installed
command is `dnaxi`.

See [REQUIREMENTS.md](REQUIREMENTS.md) for the product requirements.
Technical behavior and architecture are documented in the
[reference index](docs/README.md). Durable delivery scope lives in
[stories and epics](docs/stories/README.md); GitHub Issues track live status.

## Development

The repository uses the .NET SDK selected by `global.json`.

```bash
dotnet restore dotnet-axi.slnx
dotnet build dotnet-axi.slnx --configuration Release --no-restore
dotnet test dotnet-axi.slnx --configuration Release --no-build
```

## License

Licensed under the [Apache License 2.0](LICENSE).
