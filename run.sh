#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
if [[ -x "$HOME/.dotnet/dotnet" ]] && "$HOME/.dotnet/dotnet" --list-sdks | grep -q '^10\.'; then
  bankmanager_dotnet="$HOME/.dotnet/dotnet"
else
  bankmanager_dotnet=dotnet
fi
"$bankmanager_dotnet" build BankManager/BankManager.csproj -m:1 --disable-build-servers --verbosity quiet
exec "$bankmanager_dotnet" BankManager/bin/Debug/net10.0/BankManager.dll "$@"
