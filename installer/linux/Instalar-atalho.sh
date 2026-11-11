#!/bin/sh
# Cria (ou remove, com --remover) o atalho do BankManager no menu de aplicativos, só para o seu usuário.
# Não precisa de sudo. Use depois de extrair o pacote em uma pasta definitiva; se mover a pasta, rode de novo.
set -eu
here="$(cd -- "$(dirname -- "$0")" && pwd)"
data="${XDG_DATA_HOME:-$HOME/.local/share}"
desktop="$data/applications/bankmanager.desktop"
icon="$data/icons/hicolor/256x256/apps/bankmanager.png"
if [ "${1:-}" = "--remover" ]; then
  rm -f "$desktop" "$icon"
  command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
  echo "Atalho removido. Seus dados financeiros não foram tocados."
  exit 0
fi
# O caminho vai entre aspas no arquivo .desktop; estes caracteres têm significado especial ali e não são aceitos.
case "$here" in
  *'"'*|*'\\'*|*'%'*|*'$'*|*'`'*) echo "A pasta tem caracteres especiais ( \" \\ % \$ \` ). Mova o BankManager para um caminho mais simples e tente de novo." >&2; exit 1 ;;
esac
[ -x "$here/BankManager" ] || { echo "Não encontrei o programa BankManager nesta pasta." >&2; exit 1; }
mkdir -p "$data/applications" "$data/icons/hicolor/256x256/apps"
cp "$here/bankmanager.png" "$icon"
cat > "$desktop" <<DESKTOP
[Desktop Entry]
Type=Application
Name=BankManager
GenericName=Gestão financeira
GenericName[pt_BR]=Gestão financeira
Comment=Personal and family finances, with data kept only on your computer
Comment[pt_BR]=Finanças pessoais e familiares, com os dados só no seu computador
Exec="$here/BankManager"
Icon=bankmanager
Terminal=false
Categories=Office;Finance;
StartupWMClass=BankManager
DESKTOP
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database "$data/applications" >/dev/null 2>&1 || true
echo "Atalho criado: procure BankManager no menu de aplicativos."
echo "Para remover: $here/Instalar-atalho.sh --remover"
