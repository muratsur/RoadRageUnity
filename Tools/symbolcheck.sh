#!/usr/bin/env bash
# Catches calls to methods that are defined nowhere in the project. See
# Tools/SymbolCheck/Program.cs for why compiling the scripts with csc cannot.
#   ./Tools/symbolcheck.sh                    # check, non-zero exit if anything is undefined
#   ./Tools/symbolcheck.sh --update-baseline  # after adding a genuinely new Unity API call
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet run --project "$root/Tools/SymbolCheck" -- "$root" "$@"
