#!/usr/bin/env python3
"""Gera pacotes desktop portáteis com .NET incluído, usando apenas a biblioteca padrão do Python.

Exemplo: python3 scripts/empacotar.py --runtime linux-x64
Saídas: artifacts/publish (pastas completas) e artifacts/releases (arquivos para distribuir).
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tarfile
import xml.etree.ElementTree as ET
import zipfile

# Resolve caminhos pela localização do script, permitindo chamá-lo a partir de qualquer diretório.
RAIZ = Path(__file__).resolve().parent.parent
PROJETO = RAIZ / "codigo" / "BankManager" / "BankManager.csproj"
ALVOS = ("win-x64", "linux-x64")


# A prioridade é: caminho explícito, instalação local do usuário, comando disponível no PATH.
def caminho_do_dotnet():
    """Retorna o executável do SDK; BANKMANAGER_DOTNET permite sobrescrever a seleção."""
    substituto = os.environ.get("BANKMANAGER_DOTNET")
    if substituto:
        return substituto
    local = Path.home() / ".dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if local.is_file():
        return str(local)
    return "dotnet"


def avisos_de_terceiros(destino, alvo):
    """Copia avisos disponíveis e lista as dependências e o runtime usados na publicação."""
    # Usa o grafo realmente restaurado para registrar dependências diretas e transitivas.
    recursos = json.loads((RAIZ / "codigo" / "BankManager" / "obj" / "project.assets.json").read_text())
    pastas_de_pacotes = [Path(p) for p in recursos["packageFolders"]]
    avisos = ["BankManager — componentes de terceiros", "",
               "As licenças dos componentes abaixo continuam pertencendo aos seus autores.",
               "Esta lista é gerada do grafo NuGet usado na compilação.", ""]
    licencas = destino / "licenses"
    licencas.mkdir()
    bibliotecas = dict(recursos["libraries"])
    # Pacotes do runtime self-contained nem sempre aparecem nas bibliotecas NuGet comuns.
    # Inclui suas licenças usando a versão exata registrada no executável publicado.
    configuracao = json.loads((destino / "BankManager.runtimeconfig.json").read_text())
    for framework in configuracao["runtimeOptions"].get("includedFrameworks", []):
        nome = framework["name"] + ".Runtime." + alvo
        versao = framework["version"]
        relativo = f"{nome.lower()}/{versao}"
        pacote = next((p / relativo for p in pastas_de_pacotes if (p / relativo).is_dir()), None)
        if pacote:
            bibliotecas[f"{nome}/{versao}"] = {"type": "package", "path": relativo,
                "files": [str(p.relative_to(pacote)) for p in pacote.iterdir() if p.is_file()]}
    # O nuspec fornece atribuição e licença. Os textos disponíveis no pacote também são preservados.
    for chave, biblioteca in sorted(bibliotecas.items()):
        if biblioteca["type"] != "package":
            continue
        nome, versao = chave.rsplit("/", 1)
        pacote = next((p / biblioteca["path"] for p in pastas_de_pacotes if (p / biblioteca["path"]).is_dir()), None)
        avisos.extend([f"{nome} {versao}", f"https://www.nuget.org/packages/{nome}/{versao}"])
        if pacote:
            nuspec = next(pacote.glob("*.nuspec"), None)
            if nuspec:
                arvore = ET.parse(nuspec)
                for marca in ("authors", "copyright", "license", "licenseUrl", "projectUrl"):
                    elemento = next((e for e in arvore.iter() if e.tag.split("}")[-1] == marca), None)
                    if elemento is not None and elemento.text:
                        avisos.append(f"{marca}: {elemento.text.strip()}")
            for relativo in biblioteca.get("files", []):
                origem = pacote / relativo
                if origem.is_file() and any(word in origem.name.lower().replace("-", "").replace("_", "") for word in ("license", "thirdpartynotice", "copying", "copyright")):
                    alvo_da_copia = licencas / nome / relativo
                    alvo_da_copia.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(origem, alvo_da_copia)
        avisos.append("")
    # Complementa os avisos com a licença da fonte Inter e o texto das dependências MIT.
    for origem in (RAIZ / "docs" / "licenses").glob("*.txt"):
        shutil.copy2(origem, licencas / origem.name)
    (destino / "THIRD-PARTY-NOTICES.txt").write_text("\n".join(avisos), encoding="utf-8")


def gerar_pacote(alvo, versao):
    """Publica um runtime, inclui instruções/licenças e cria seu arquivo e checksum.

    runtime deve ser um dos RUNTIMES e version deve ter sido validada por main.
    Recria apenas a pasta de publicação desse nome dentro de artifacts/publish.
    Uma falha de dotnet publish interrompe a função antes de compactar a saída.
    """
    nome = f"BankManager-{versao}-{alvo}"
    destino = RAIZ / "artifacts" / "publish" / nome
    pasta_de_pacotes = RAIZ / "artifacts" / "releases"
    # Remove somente a saída anterior deste destino para evitar distribuir arquivos obsoletos.
    if destino.exists():
        shutil.rmtree(destino)
    destino.mkdir(parents=True)
    pasta_de_pacotes.mkdir(parents=True, exist_ok=True)
    # Inclui o runtime; mantém DLLs separadas e desativa trimming para preservar componentes dinâmicos.
    subprocess.run([
        caminho_do_dotnet(), "publish", str(PROJETO), "-c", "Release", "-r", alvo,
        "--self-contained", "true", "-o", str(destino), "-m:1", "--disable-build-servers",
        f"-p:Version={versao}", "-p:PublishSingleFile=false", "-p:PublishTrimmed=false",
        "-p:DebugType=None", "-p:DebugSymbols=false", "-p:NuGetAudit=true",
    ], cwd=RAIZ, check=True)
    shutil.copy2(RAIZ / "docs" / "LEIA-ME.txt", destino / "LEIA-ME.txt")
    avisos_de_terceiros(destino, alvo)
    # TAR preserva permissões de execução no Linux; o ZIP atende à extração nativa no Windows.
    if alvo == "linux-x64":
        executavel = destino / "BankManager"
        executavel.chmod(0o755)
        iniciador = destino / "Iniciar.sh"
        # O iniciador muda para sua própria pasta para funcionar mesmo quando aberto de outro diretório.
        iniciador.write_text('#!/bin/sh\nset -eu\ncd -- "$(dirname -- "$0")"\nexec ./BankManager "$@"\n', encoding="utf-8")
        iniciador.chmod(0o755)
        # Atalho no menu de aplicativos (só do usuário, sem sudo): script e ícone acompanham o pacote portátil.
        shutil.copy2(RAIZ / "instalador" / "linux" / "Instalar-atalho.sh", destino / "Instalar-atalho.sh")
        (destino / "Instalar-atalho.sh").chmod(0o755)
        shutil.copy2(RAIZ / "codigo" / "BankManager" / "Recursos" / "icone.png", destino / "bankmanager.png")
        arquivo_compactado = pasta_de_pacotes / f"{nome}.tar.gz"
        with tarfile.open(arquivo_compactado, "w:gz") as saida:
            saida.add(destino, arcname=nome)
    else:
        arquivo_compactado = pasta_de_pacotes / f"{nome}.zip"
        with zipfile.ZipFile(arquivo_compactado, "w", zipfile.ZIP_DEFLATED) as saida:
            for caminho in sorted(destino.rglob("*")):
                if caminho.is_file():
                    saida.write(caminho, caminho.relative_to(destino.parent))
    # Calcula o hash depois de finalizar a compactação; alterações no pacote mudam o checksum.
    with arquivo_compactado.open("rb") as origem:
        resumo = hashlib.file_digest(origem, "sha256").hexdigest()
    arquivo_compactado.with_name(arquivo_compactado.name + ".sha256").write_text(f"{resumo}  {arquivo_compactado.name}\n", encoding="ascii")
    print(f"\nPronto: {arquivo_compactado} ({arquivo_compactado.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def principal():
    """Lê runtime/versão da linha de comando e publica um destino ou todos os destinos suportados."""
    analisador = argparse.ArgumentParser(description=__doc__)
    analisador.add_argument("--runtime", choices=(*ALVOS, "all"), default="all")
    analisador.add_argument("--version", default=ET.parse(PROJETO).findtext("./PropertyGroup/Version"))
    argumentos = analisador.parse_args()
    # A versão também faz parte de caminhos; aceitar só SemVer evita componentes de caminho arbitrários.
    if not argumentos.version or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", argumentos.version):
        analisador.error("A versão deve ser SemVer, por exemplo 1.0.0 ou 1.1.0-beta.1.")
    for alvo in ALVOS if argumentos.runtime == "all" else (argumentos.runtime,):
        gerar_pacote(alvo, argumentos.version)


if __name__ == "__main__":
    principal()
