#!/usr/bin/env python3
"""Gera o instalador do Windows (Inno Setup) e o AppImage do Linux, a partir da pasta publicada por empacotar.py.

Uso (rode antes: python3 scripts/empacotar.py --runtime <alvo>):
    python3 scripts/instaladores.py windows     # precisa do Inno Setup (ISCC.exe), normalmente em um Windows
    python3 scripts/instaladores.py appimage    # precisa de Linux x86_64; baixa o appimagetool fixado e confere o checksum

Saídas em artifacts/releases, com arquivos .sha256. O aplicativo não tem atualizador interno (ele nunca acessa a internet):
o instalador do Windows atualiza por cima da versão instalada, e o AppImage carrega a informação de atualização que a
ferramenta externa AppImageUpdate usa quando o próprio usuário a executa.
"""
import argparse
import hashlib
import os
from pathlib import Path
import platform
import re
import shutil
import stat
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "codigo" / "BankManager" / "BankManager.csproj"
RELEASES = ROOT / "artifacts" / "releases"
# Ferramenta fixada por versão e checksum: um download alterado é recusado antes de ser executado.
APPIMAGETOOL_URL = "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
APPIMAGETOOL_SHA256 = "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
# Onde o AppImageUpdate procura versões novas (somente quando o usuário o executa; o aplicativo não faz isso).
UPDATE_INFORMATION = "gh-releases-zsync|Muzi3060|BankManager|latest|BankManager-*-x86_64.AppImage.zsync"


def sha256_of(path):
    """Calcula o SHA-256 de um arquivo."""
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


def write_checksum(path):
    """Grava ao lado do arquivo um .sha256 no formato aceito por sha256sum -c."""
    path.with_name(path.name + ".sha256").write_text(f"{sha256_of(path)}  {path.name}\n", encoding="ascii")


def published(version, runtime):
    """Localiza a pasta publicada por empacotar.py, ou explica como gerá-la."""
    folder = ROOT / "artifacts" / "publish" / f"BankManager-{version}-{runtime}"
    if not (folder / "BankManager.runtimeconfig.json").is_file():
        sys.exit(f"Pasta publicada não encontrada: {folder}\nRode antes: python3 scripts/empacotar.py --runtime {runtime}")
    return folder


def numeric_version(version):
    """Converte 2.0.0 ou 2.1.0-beta.1 para x.y.z.0, o formato numérico que o Windows exige nas propriedades do arquivo."""
    return ".".join(re.match(r"\d+\.\d+\.\d+", version).group(0).split(".") + ["0"])


def find_iscc():
    """Procura o compilador do Inno Setup: variável BANKMANAGER_ISCC, PATH e pastas comuns de instalação."""
    override = os.environ.get("BANKMANAGER_ISCC")
    candidates = [override] if override else []
    candidates.append(shutil.which("iscc") or shutil.which("ISCC"))
    for base in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles"), os.environ.get("LOCALAPPDATA") and Path(os.environ["LOCALAPPDATA"]) / "Programs"):
        if base:
            candidates.extend(sorted(Path(base).glob("Inno Setup*/ISCC.exe"), reverse=True))
    found = next((Path(c) for c in candidates if c and Path(c).is_file()), None)
    if not found:
        sys.exit("Inno Setup não encontrado. Instale (winget install JRSoftware.InnoSetup, ou choco install innosetup) "
                 "ou aponte BANKMANAGER_ISCC para o ISCC.exe.")
    return found


def windows(version):
    """Compila o instalador do Windows com o Inno Setup."""
    source = published(version, "win-x64")
    RELEASES.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(find_iscc()), "/Qp", f"/DAppVersion={version}", f"/DNumericVersion={numeric_version(version)}",
                    f"/DSourceDir={source}", f"/DIconFile={ROOT / 'codigo' / 'BankManager' / 'Recursos' / 'icone.ico'}",
                    f"/DOutputDir={RELEASES}", str(ROOT / "instalador" / "windows" / "BankManager.iss")], check=True)
    setup = RELEASES / f"BankManager-{version}-win-x64-setup.exe"
    write_checksum(setup)
    print(f"\nPronto: {setup} ({setup.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def fetch_appimagetool():
    """Baixa o appimagetool fixado (uma vez) e confere o checksum; um arquivo diferente do esperado é apagado e recusado."""
    tool = ROOT / "artifacts" / "tools" / "appimagetool-1.9.1-x86_64.AppImage"
    tool.parent.mkdir(parents=True, exist_ok=True)
    if not tool.is_file():
        print(f"Baixando {APPIMAGETOOL_URL}", flush=True)
        urllib.request.urlretrieve(APPIMAGETOOL_URL, tool)
    if sha256_of(tool) != APPIMAGETOOL_SHA256:
        tool.unlink()
        sys.exit("O checksum do appimagetool não confere com o valor fixado neste script. O arquivo foi descartado.")
    tool.chmod(tool.stat().st_mode | stat.S_IXUSR)
    return tool


def build_appdir(version):
    """Monta o AppDir (aplicativo, AppRun, atalho, ícone e metadados), pronto para o appimagetool, e devolve seu caminho."""
    source = published(version, "linux-x64")
    appdir = ROOT / "artifacts" / "appimage" / "BankManager.AppDir"
    if appdir.exists():
        shutil.rmtree(appdir)
    program = appdir / "usr" / "lib" / "bankmanager"
    shutil.copytree(source, program)
    (program / "BankManager").chmod(0o755)
    # Arquivos do launcher de pasta portátil não fazem sentido dentro do AppImage.
    for leftover in ("Iniciar.sh", "Instalar-atalho.sh", "bankmanager.png"):
        (program / leftover).unlink(missing_ok=True)
    assets = ROOT / "instalador" / "linux"
    shutil.copy2(assets / "AppRun", appdir / "AppRun")
    (appdir / "AppRun").chmod(0o755)
    icon = ROOT / "codigo" / "BankManager" / "Recursos" / "icone.png"
    shutil.copy2(assets / "bankmanager.desktop", appdir / "bankmanager.desktop")
    shutil.copy2(icon, appdir / "bankmanager.png")
    shutil.copy2(icon, appdir / ".DirIcon")
    # Cópias nos caminhos padrão de XDG, usadas pelas lojas de aplicativos e pelo integrador de desktop.
    for relative, origin in (("usr/share/applications/bankmanager.desktop", assets / "bankmanager.desktop"),
                             ("usr/share/metainfo/io.github.Muzi3060.BankManager.appdata.xml", assets / "bankmanager.appdata.xml"),
                             ("usr/share/icons/hicolor/256x256/apps/bankmanager.png", icon)):
        (appdir / relative).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(origin, appdir / relative)
    return appdir


def appimage(version):
    """Empacota o AppDir em um AppImage com informação de atualização (zsync) e checksums."""
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
        sys.exit("O AppImage só pode ser gerado em Linux x86_64.")
    appdir = build_appdir(version)
    RELEASES.mkdir(parents=True, exist_ok=True)
    output = RELEASES / f"BankManager-{version}-x86_64.AppImage"
    # --appimage-extract-and-run dispensa o FUSE, que servidores de CI e sistemas mínimos costumam não ter.
    subprocess.run([str(fetch_appimagetool()), "--appimage-extract-and-run", "--no-appstream", "-u", UPDATE_INFORMATION, str(appdir), str(output)],
                   cwd=RELEASES, check=True, env={**os.environ, "ARCH": "x86_64"})
    for artifact in (output, output.with_name(output.name + ".zsync")):
        if artifact.is_file():
            write_checksum(artifact)
    print(f"\nPronto: {output} ({output.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def main():
    """Lê o alvo e a versão da linha de comando."""
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("target", choices=("windows", "appimage"))
    parser.add_argument("--version", default=ET.parse(PROJECT).findtext("./PropertyGroup/Version"))
    args = parser.parse_args()
    if not args.version or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", args.version):
        parser.error("A versão deve ser SemVer, por exemplo 2.0.0 ou 2.1.0-beta.1.")
    {"windows": windows, "appimage": appimage}[args.target](args.version)


if __name__ == "__main__":
    main()
