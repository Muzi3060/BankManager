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

RAIZ = Path(__file__).resolve().parent.parent
PROJETO = RAIZ / "codigo" / "BankManager" / "BankManager.csproj"
PASTA_DE_PACOTES = RAIZ / "artifacts" / "releases"
# Ferramenta fixada por versão e checksum: um download alterado é recusado antes de ser executado.
URL_DO_APPIMAGETOOL = "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"
SHA256_DO_APPIMAGETOOL = "ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0"
# Onde o AppImageUpdate procura versões novas (somente quando o usuário o executa; o aplicativo não faz isso).
INFORMACAO_DE_ATUALIZACAO = "gh-releases-zsync|Muzi3060|BankManager|latest|BankManager-*-x86_64.AppImage.zsync"


def sha256_de(caminho):
    """Calcula o SHA-256 de um arquivo."""
    with caminho.open("rb") as origem:
        return hashlib.file_digest(origem, "sha256").hexdigest()


def gravar_checksum(caminho):
    """Grava ao lado do arquivo um .sha256 no formato aceito por sha256sum -c."""
    caminho.with_name(caminho.name + ".sha256").write_text(f"{sha256_de(caminho)}  {caminho.name}\n", encoding="ascii")


def pasta_publicada(versao, alvo):
    """Localiza a pasta publicada por empacotar.py, ou explica como gerá-la."""
    pasta = RAIZ / "artifacts" / "publish" / f"BankManager-{versao}-{alvo}"
    if not (pasta / "BankManager.runtimeconfig.json").is_file():
        sys.exit(f"Pasta publicada não encontrada: {pasta}\nRode antes: python3 scripts/empacotar.py --runtime {alvo}")
    return pasta


def versao_numerica(versao):
    """Converte 2.0.0 ou 2.1.0-beta.1 para x.y.z.0, o formato numérico que o Windows exige nas propriedades do arquivo."""
    return ".".join(re.match(r"\d+\.\d+\.\d+", versao).group(0).split(".") + ["0"])


def achar_iscc():
    """Procura o compilador do Inno Setup: variável BANKMANAGER_ISCC, PATH e pastas comuns de instalação."""
    substituto = os.environ.get("BANKMANAGER_ISCC")
    candidatos = [substituto] if substituto else []
    candidatos.append(shutil.which("iscc") or shutil.which("ISCC"))
    for base in (os.environ.get("ProgramFiles(x86)"), os.environ.get("ProgramFiles"), os.environ.get("LOCALAPPDATA") and Path(os.environ["LOCALAPPDATA"]) / "Programs"):
        if base:
            candidatos.extend(sorted(Path(base).glob("Inno Setup*/ISCC.exe"), reverse=True))
    encontrado = next((Path(c) for c in candidatos if c and Path(c).is_file()), None)
    if not encontrado:
        sys.exit("Inno Setup não encontrado. Instale (winget install JRSoftware.InnoSetup, ou choco install innosetup) "
                 "ou aponte BANKMANAGER_ISCC para o ISCC.exe.")
    return encontrado


def gerar_instalador_windows(versao):
    """Compila o instalador do Windows com o Inno Setup."""
    origem = pasta_publicada(versao, "win-x64")
    PASTA_DE_PACOTES.mkdir(parents=True, exist_ok=True)
    subprocess.run([str(achar_iscc()), "/Qp", f"/DVersaoDoApp={versao}", f"/DVersaoNumerica={versao_numerica(versao)}",
                    f"/DPastaDeOrigem={origem}", f"/DArquivoDoIcone={RAIZ / 'codigo' / 'BankManager' / 'Recursos' / 'icone.ico'}",
                    f"/DPastaDeSaida={PASTA_DE_PACOTES}", str(RAIZ / "instalador" / "windows" / "BankManager.iss")], check=True)
    instalador = PASTA_DE_PACOTES / f"BankManager-{versao}-win-x64-setup.exe"
    gravar_checksum(instalador)
    print(f"\nPronto: {instalador} ({instalador.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def baixar_appimagetool():
    """Baixa o appimagetool fixado (uma vez) e confere o checksum; um arquivo diferente do esperado é apagado e recusado."""
    ferramenta = RAIZ / "artifacts" / "tools" / "appimagetool-1.9.1-x86_64.AppImage"
    ferramenta.parent.mkdir(parents=True, exist_ok=True)
    if not ferramenta.is_file():
        print(f"Baixando {URL_DO_APPIMAGETOOL}", flush=True)
        urllib.request.urlretrieve(URL_DO_APPIMAGETOOL, ferramenta)
    if sha256_de(ferramenta) != SHA256_DO_APPIMAGETOOL:
        ferramenta.unlink()
        sys.exit("O checksum do appimagetool não confere com o valor fixado neste script. O arquivo foi descartado.")
    ferramenta.chmod(ferramenta.stat().st_mode | stat.S_IXUSR)
    return ferramenta


def montar_pasta_do_app(versao):
    """Monta o AppDir (aplicativo, AppRun, atalho, ícone e metadados), pronto para o appimagetool, e devolve seu caminho."""
    origem = pasta_publicada(versao, "linux-x64")
    pasta_do_app = RAIZ / "artifacts" / "appimage" / "BankManager.AppDir"
    if pasta_do_app.exists():
        shutil.rmtree(pasta_do_app)
    programa = pasta_do_app / "usr" / "lib" / "bankmanager"
    shutil.copytree(origem, programa)
    (programa / "BankManager").chmod(0o755)
    # Arquivos do launcher de pasta portátil não fazem sentido dentro do AppImage.
    for sobra in ("Iniciar.sh", "Instalar-atalho.sh", "bankmanager.png"):
        (programa / sobra).unlink(missing_ok=True)
    recursos = RAIZ / "instalador" / "linux"
    shutil.copy2(recursos / "AppRun", pasta_do_app / "AppRun")
    (pasta_do_app / "AppRun").chmod(0o755)
    icone = RAIZ / "codigo" / "BankManager" / "Recursos" / "icone.png"
    shutil.copy2(recursos / "bankmanager.desktop", pasta_do_app / "bankmanager.desktop")
    shutil.copy2(icone, pasta_do_app / "bankmanager.png")
    shutil.copy2(icone, pasta_do_app / ".DirIcon")
    # Cópias nos caminhos padrão de XDG, usadas pelas lojas de aplicativos e pelo integrador de desktop.
    for relativo, origem_do_arquivo in (("usr/share/applications/bankmanager.desktop", recursos / "bankmanager.desktop"),
                             ("usr/share/metainfo/io.github.Muzi3060.BankManager.appdata.xml", recursos / "bankmanager.appdata.xml"),
                             ("usr/share/icons/hicolor/256x256/apps/bankmanager.png", icone)):
        (pasta_do_app / relativo).parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(origem_do_arquivo, pasta_do_app / relativo)
    return pasta_do_app


def gerar_appimage(versao):
    """Empacota o AppDir em um AppImage com informação de atualização (zsync) e checksums."""
    if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
        sys.exit("O AppImage só pode ser gerado em Linux x86_64.")
    pasta_do_app = montar_pasta_do_app(versao)
    PASTA_DE_PACOTES.mkdir(parents=True, exist_ok=True)
    saida = PASTA_DE_PACOTES / f"BankManager-{versao}-x86_64.AppImage"
    # --appimage-extract-and-run dispensa o FUSE, que servidores de CI e sistemas mínimos costumam não ter.
    subprocess.run([str(baixar_appimagetool()), "--appimage-extract-and-run", "--no-appstream", "-u", INFORMACAO_DE_ATUALIZACAO, str(pasta_do_app), str(saida)],
                   cwd=PASTA_DE_PACOTES, check=True, env={**os.environ, "ARCH": "x86_64"})
    for artefato in (saida, saida.with_name(saida.name + ".zsync")):
        if artefato.is_file():
            gravar_checksum(artefato)
    print(f"\nPronto: {saida} ({saida.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def principal():
    """Lê o alvo e a versão da linha de comando."""
    analisador = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    analisador.add_argument("target", choices=("windows", "appimage"))
    analisador.add_argument("--version", default=ET.parse(PROJETO).findtext("./PropertyGroup/Version"))
    argumentos = analisador.parse_args()
    if not argumentos.version or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", argumentos.version):
        analisador.error("A versão deve ser SemVer, por exemplo 2.0.0 ou 2.1.0-beta.1.")
    {"windows": gerar_instalador_windows, "appimage": gerar_appimage}[argumentos.target](argumentos.version)


if __name__ == "__main__":
    principal()
