#!/usr/bin/env bash
# Iniciador para desenvolvimento, não para o pacote final: falhas interrompem a execução.
set -euo pipefail
# Normaliza a pasta antes de usar caminhos relativos da solução.
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
# Prefere a instalação local se ela contém o SDK 10, mesmo quando o PATH aponta para um SDK antigo.
if [[ -x "$HOME/.dotnet/dotnet" ]] && "$HOME/.dotnet/dotnet" --list-sdks | grep -q '^10\.'; then
  bankmanager_dotnet="$HOME/.dotnet/dotnet"
else
  bankmanager_dotnet=dotnet
fi
# Compila antes de abrir; exec repassa sinais e código de saída ao processo do aplicativo.
"$bankmanager_dotnet" build BankManager/BankManager.csproj -m:1 --disable-build-servers --verbosity quiet
exec "$bankmanager_dotnet" BankManager/bin/Debug/net10.0/BankManager.dll "$@"
