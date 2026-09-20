$ErrorActionPreference = 'Stop'

& dotnet build Workspace.slnx --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

[xml]$verifierProject = Get-Content .benchmark-validation/Verifier.csproj
$frameworks = $verifierProject.Project.PropertyGroup.TargetFrameworks
if ([string]::IsNullOrWhiteSpace($frameworks)) {
    $frameworks = $verifierProject.Project.PropertyGroup.TargetFramework
}

foreach ($framework in $frameworks -split ';') {
    & dotnet run `
        --project .benchmark-validation/Verifier.csproj `
        --configuration Release `
        --framework $framework `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
