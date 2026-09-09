Set-Location "$PSScriptRoot\.."
param(
    [string]$RepositoryPath = "."
)

dotnet run --project .\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj -- index $RepositoryPath