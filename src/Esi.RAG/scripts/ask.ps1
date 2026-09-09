param(
	[Parameter(Mandatory = $true)]
	[string]$Query
)

$ErrorActionPreference = "Stop"
dotnet run --project "$PSScriptRoot\..\src\Esi.RAG.Cli\Esi.RAG.Cli.csproj" -- ask $Query
