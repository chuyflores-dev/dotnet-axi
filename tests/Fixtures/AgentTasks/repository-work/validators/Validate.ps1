$ErrorActionPreference = 'Stop'

& dotnet build Workspace.slnx --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& dotnet run `
    --project .benchmark-validation/Verifier.csproj `
    --configuration Release `
    --nologo
exit $LASTEXITCODE
