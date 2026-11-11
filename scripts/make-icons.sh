#!/usr/bin/env bash
# Gera icon.png (janela) e icon.ico (executável do Windows) a partir de icon.svg.
# Requer rsvg-convert e ImageMagick. Os arquivos gerados ficam versionados, então só é preciso
# rodar este script quando o desenho do ícone mudar.
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../BankManager/Assets"
rsvg-convert -w 256 -h 256 icon.svg -o icon.png
for size in 16 32 48 64 128 256; do rsvg-convert -w "$size" -h "$size" icon.svg -o "/tmp/bm-icon-$size.png"; done
magick /tmp/bm-icon-16.png /tmp/bm-icon-32.png /tmp/bm-icon-48.png /tmp/bm-icon-64.png /tmp/bm-icon-128.png /tmp/bm-icon-256.png icon.ico
rm -f /tmp/bm-icon-*.png
