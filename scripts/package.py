#!/usr/bin/env python3
"""Gera pacotes desktop portáteis com .NET incluído, usando apenas a biblioteca padrão do Python.

Exemplo: python3 scripts/package.py --runtime linux-x64
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
ROOT = Path(__file__).resolve().parent.parent
PROJECT = ROOT / "BankManager" / "BankManager.csproj"
RUNTIMES = ("win-x64", "linux-x64")


# A prioridade é: caminho explícito, instalação local do usuário, comando disponível no PATH.
def dotnet():
    """Retorna o executável do SDK; BANKMANAGER_DOTNET permite sobrescrever a seleção."""
    override = os.environ.get("BANKMANAGER_DOTNET")
    if override:
        return override
    local = Path.home() / ".dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if local.is_file():
        return str(local)
    return "dotnet"


def third_party_notices(destination, runtime):
    """Copia avisos disponíveis e lista as dependências e o runtime usados na publicação."""
    # Usa o grafo realmente restaurado para registrar dependências diretas e transitivas.
    assets = json.loads((ROOT / "BankManager" / "obj" / "project.assets.json").read_text())
    package_roots = [Path(p) for p in assets["packageFolders"]]
    notices = ["BankManager — componentes de terceiros", "",
               "As licenças dos componentes abaixo continuam pertencendo aos seus autores.",
               "Esta lista é gerada do grafo NuGet usado na compilação.", ""]
    licenses = destination / "licenses"
    licenses.mkdir()
    libraries = dict(assets["libraries"])
    # Pacotes do runtime self-contained nem sempre aparecem nas bibliotecas NuGet comuns.
    # Inclui suas licenças usando a versão exata registrada no executável publicado.
    runtime_config = json.loads((destination / "BankManager.runtimeconfig.json").read_text())
    for framework in runtime_config["runtimeOptions"].get("includedFrameworks", []):
        name = framework["name"] + ".Runtime." + runtime
        version = framework["version"]
        relative = f"{name.lower()}/{version}"
        package = next((p / relative for p in package_roots if (p / relative).is_dir()), None)
        if package:
            libraries[f"{name}/{version}"] = {"type": "package", "path": relative,
                "files": [str(p.relative_to(package)) for p in package.iterdir() if p.is_file()]}
    # O nuspec fornece atribuição e licença. Os textos disponíveis no pacote também são preservados.
    for key, library in sorted(libraries.items()):
        if library["type"] != "package":
            continue
        name, version = key.rsplit("/", 1)
        package = next((p / library["path"] for p in package_roots if (p / library["path"]).is_dir()), None)
        notices.extend([f"{name} {version}", f"https://www.nuget.org/packages/{name}/{version}"])
        if package:
            nuspec = next(package.glob("*.nuspec"), None)
            if nuspec:
                tree = ET.parse(nuspec)
                for tag in ("authors", "copyright", "license", "licenseUrl", "projectUrl"):
                    element = next((e for e in tree.iter() if e.tag.split("}")[-1] == tag), None)
                    if element is not None and element.text:
                        notices.append(f"{tag}: {element.text.strip()}")
            for relative in library.get("files", []):
                source = package / relative
                if source.is_file() and any(word in source.name.lower().replace("-", "").replace("_", "") for word in ("license", "thirdpartynotice", "copying", "copyright")):
                    target = licenses / name / relative
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(source, target)
        notices.append("")
    # Complementa os avisos com a licença da fonte Inter e o texto das dependências MIT.
    for source in (ROOT / "docs" / "licenses").glob("*.txt"):
        shutil.copy2(source, licenses / source.name)
    (destination / "THIRD-PARTY-NOTICES.txt").write_text("\n".join(notices), encoding="utf-8")


def package(runtime, version):
    """Publica um runtime, inclui instruções/licenças e cria seu arquivo e checksum.

    runtime deve ser um dos RUNTIMES e version deve ter sido validada por main.
    Recria apenas a pasta de publicação desse nome dentro de artifacts/publish.
    Uma falha de dotnet publish interrompe a função antes de compactar a saída.
    """
    name = f"BankManager-{version}-{runtime}"
    destination = ROOT / "artifacts" / "publish" / name
    releases = ROOT / "artifacts" / "releases"
    # Remove somente a saída anterior deste destino para evitar distribuir arquivos obsoletos.
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir(parents=True)
    releases.mkdir(parents=True, exist_ok=True)
    # Inclui o runtime; mantém DLLs separadas e desativa trimming para preservar componentes dinâmicos.
    subprocess.run([
        dotnet(), "publish", str(PROJECT), "-c", "Release", "-r", runtime,
        "--self-contained", "true", "-o", str(destination), "-m:1", "--disable-build-servers",
        f"-p:Version={version}", "-p:PublishSingleFile=false", "-p:PublishTrimmed=false",
        "-p:DebugType=None", "-p:DebugSymbols=false", "-p:NuGetAudit=true",
    ], cwd=ROOT, check=True)
    shutil.copy2(ROOT / "docs" / "LEIA-ME.txt", destination / "LEIA-ME.txt")
    third_party_notices(destination, runtime)
    # TAR preserva permissões de execução no Linux; o ZIP atende à extração nativa no Windows.
    if runtime == "linux-x64":
        executable = destination / "BankManager"
        executable.chmod(0o755)
        launcher = destination / "Iniciar.sh"
        # O iniciador muda para sua própria pasta para funcionar mesmo quando aberto de outro diretório.
        launcher.write_text('#!/bin/sh\nset -eu\ncd -- "$(dirname -- "$0")"\nexec ./BankManager "$@"\n', encoding="utf-8")
        launcher.chmod(0o755)
        archive = releases / f"{name}.tar.gz"
        with tarfile.open(archive, "w:gz") as output:
            output.add(destination, arcname=name)
    else:
        archive = releases / f"{name}.zip"
        with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as output:
            for path in sorted(destination.rglob("*")):
                if path.is_file():
                    output.write(path, path.relative_to(destination.parent))
    # Calcula o hash depois de finalizar a compactação; alterações no pacote mudam o checksum.
    with archive.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    archive.with_name(archive.name + ".sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
    print(f"\nPronto: {archive} ({archive.stat().st_size / 1024 / 1024:.1f} MiB)", flush=True)


def main():
    """Lê runtime/versão da linha de comando e publica um destino ou todos os destinos suportados."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", choices=(*RUNTIMES, "all"), default="all")
    parser.add_argument("--version", default=ET.parse(PROJECT).findtext("./PropertyGroup/Version"))
    args = parser.parse_args()
    # A versão também faz parte de caminhos; aceitar só SemVer evita componentes de caminho arbitrários.
    if not args.version or not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?", args.version):
        parser.error("A versão deve ser SemVer, por exemplo 1.0.0 ou 1.1.0-beta.1.")
    for runtime in RUNTIMES if args.runtime == "all" else (args.runtime,):
        package(runtime, args.version)


if __name__ == "__main__":
    main()
