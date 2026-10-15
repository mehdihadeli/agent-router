# Repo-local development launcher for the Agent Router CLI.
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $GatewayArgs
)

$project = Join-Path $PSScriptRoot "src/AgentRouter.Cli/AgentRouter.Cli.csproj"
dotnet run --project $project --no-restore -v:q -- @GatewayArgs
exit $LASTEXITCODE