# Catches calls to methods that are defined nowhere in the project. See
# Tools/SymbolCheck/Program.cs for why compiling the scripts with csc cannot.
#   .\Tools\symbolcheck.ps1                    # check, non-zero exit if anything is undefined
#   .\Tools\symbolcheck.ps1 --update-baseline  # after adding a genuinely new Unity API call
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet run --project (Join-Path $root "Tools\SymbolCheck") -- $root @args
exit $LASTEXITCODE
