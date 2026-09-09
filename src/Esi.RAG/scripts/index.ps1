param(
	[string]$RepositoryPath = "D:\Git\Esi.RAG"
)

$ErrorActionPreference = "Stop"
dotnet run --project "$PSScriptRoot\..\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj" -- index $RepositoryPath
