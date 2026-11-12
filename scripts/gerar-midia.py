#!/usr/bin/env python3
"""Gera os GIFs de demonstração do README (docs/midia/) com um comando: python3 scripts/gerar-midia.py

Como funciona:
1. O executável de testes de interface roda em "modo de mídia" (variáveis BANKMANAGER_MIDIA e BANKMANAGER_MIDIA_CENA). Ele abre a
   janela real com dados FICTÍCIOS, em uma pasta temporária criada por ele mesmo, executa um roteiro curto e salva um quadro (PNG)
   por passo, mais um arquivo roteiro.tsv com a legenda, o tempo de exibição e a área do controle clicado.
2. Este script monta cada GIF: destaca o controle, reduz para 900 px de largura, escreve a legenda no rodapé, cria uma transição
   curta entre os passos e grava com uma paleta única e otimizada.

Precisa de: .NET SDK, uma sessão gráfica (a janela é real) e Python com Pillow (pip install pillow). Nada é baixado da internet.
Os quadros ficam em uma pasta temporária, apagada ao final; só os GIFs vão para docs/midia/.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

try:
    from PIL import Image, ImageDraw, ImageFont
except ImportError:
    sys.exit("Falta a biblioteca Pillow. Instale com: python3 -m pip install pillow")

RAIZ = Path(__file__).resolve().parent.parent
PROJETO_DE_TESTES = RAIZ / "testes" / "BankManager.TestesDeInterface"
DESTINO = RAIZ / "docs" / "midia"
# Cada GIF é a sequência de uma ou mais cenas do roteiro de mídia.
GIFS = {
    "visao-geral": ["visao-geral"],
    "criar-despesa": ["criar-despesa"],
    "temas": ["temas"],
    "compartilhar": ["compartilhar-enviar", "compartilhar-receber"],
}
LARGURA = 900                  # largura final, em pixels
ALTURA_DA_LEGENDA = 46         # faixa escura no rodapé
LIMITE_EM_BYTES = 3 * 1024 * 1024
DURACAO_DA_TRANSICAO = 90      # milissegundos por quadro de transição (cerca de 11 quadros por segundo)
QUADROS_DE_TRANSICAO = 2
COR_DA_LEGENDA = (20, 22, 26)
COR_DO_DESTAQUE = (18, 160, 114)


def fonte(tamanho):
    """Procura uma fonte sem serifa em negrito no sistema (fc-match); sem ela, usa a fonte embutida do Pillow."""
    try:
        caminho = subprocess.run(["fc-match", "-f", "%{file}", "sans:bold"], capture_output=True, text=True, check=True).stdout.strip()
        return ImageFont.truetype(caminho, tamanho)
    except (OSError, subprocess.CalledProcessError):
        return ImageFont.load_default(tamanho)


def gravar_quadros(cena, pasta, arquivo_compartilhado):
    """Roda o roteiro de uma cena no executável de testes de interface e devolve a pasta com os quadros."""
    ambiente = dict(os.environ, BANKMANAGER_MIDIA=str(pasta), BANKMANAGER_MIDIA_CENA=cena, BANKMANAGER_MIDIA_ARQUIVO=str(arquivo_compartilhado))
    # A pasta de dados é sempre uma pasta temporária criada pelo próprio teste; uma variável herdada do terminal não deve interferir.
    ambiente.pop("BANKMANAGER_DATA_DIR", None)
    subprocess.run(["dotnet", "run", "--no-build", "--project", str(PROJETO_DE_TESTES)], check=True, env=ambiente, stdout=subprocess.DEVNULL)
    return pasta / cena


def ler_roteiro(pasta_da_cena):
    """Lê roteiro.tsv: uma linha por quadro, com arquivo, legenda, pausa em milissegundos e área do destaque (x,y,largura,altura)."""
    passos = []
    for linha in (pasta_da_cena / "roteiro.tsv").read_text(encoding="utf-8").splitlines():
        arquivo, legenda, pausa, area = (linha.split("\t") + [""])[:4]
        passos.append((pasta_da_cena / arquivo, legenda, int(pausa), tuple(int(v) for v in area.split(",")) if area else None))
    return passos


def compor(passos):
    """Transforma os quadros brutos em imagens prontas: mesma tela, destaque, redução e legenda. Devolve [(imagem, pausa)]."""
    tamanho_da_janela = Image.open(passos[0][0]).size
    escala = LARGURA / tamanho_da_janela[0]
    altura = round(tamanho_da_janela[1] * escala)
    letra = fonte(18)
    fundo = None
    prontos = []
    for arquivo, legenda, pausa, area in passos:
        quadro = Image.open(arquivo).convert("RGB")
        deslocamento = (0, 0)
        if quadro.size == tamanho_da_janela:
            fundo = quadro
        else:
            # Diálogo em janela própria (menor): aparece centralizado sobre a última tela principal, escurecida.
            base = Image.blend(fundo, Image.new("RGB", tamanho_da_janela, (20, 24, 40)), 0.42)
            deslocamento = ((tamanho_da_janela[0] - quadro.width) // 2, max(0, (tamanho_da_janela[1] - quadro.height) // 2))
            base.paste(quadro, deslocamento)
            quadro = base
        if area:
            # Não há cursor nos quadros: o controle clicado ganha um contorno verde (com um fio branco por fora, para aparecer no tema escuro).
            x, y, largura, altura_da_area = area
            x += deslocamento[0]
            y += deslocamento[1]
            pincel = ImageDraw.Draw(quadro)
            pincel.rounded_rectangle((x - 8, y - 8, x + largura + 8, y + altura_da_area + 8), radius=16, outline=(255, 255, 255), width=2)
            pincel.rounded_rectangle((x - 6, y - 6, x + largura + 6, y + altura_da_area + 6), radius=14, outline=COR_DO_DESTAQUE, width=5)
        tela = Image.new("RGB", (LARGURA, altura + ALTURA_DA_LEGENDA), COR_DA_LEGENDA)
        tela.paste(quadro.resize((LARGURA, altura), Image.Resampling.LANCZOS), (0, 0))
        pincel = ImageDraw.Draw(tela)
        largura_do_texto = pincel.textlength(legenda, font=letra)
        pincel.text(((LARGURA - largura_do_texto) / 2, altura + (ALTURA_DA_LEGENDA - 24) / 2), legenda, font=letra, fill=(255, 255, 255))
        prontos.append((tela, pausa))
    return prontos


def gravar_gif(prontos, destino, largura, cores):
    """Grava o GIF em laço: cada passo fica parado pelo tempo do roteiro e entre dois passos há uma transição curta."""
    imagens, duracoes = [], []
    for indice, (tela, pausa) in enumerate(prontos):
        if indice > 0:
            anterior = prontos[indice - 1][0]
            for parte in range(1, QUADROS_DE_TRANSICAO + 1):
                imagens.append(Image.blend(anterior, tela, parte / (QUADROS_DE_TRANSICAO + 1)))
                duracoes.append(DURACAO_DA_TRANSICAO)
        imagens.append(tela)
        duracoes.append(pausa)
    if largura != LARGURA:
        imagens = [i.resize((largura, round(i.height * largura / i.width)), Image.Resampling.LANCZOS) for i in imagens]
    # Paleta única para o GIF inteiro, calculada sobre uma amostra de todos os passos; sem pontilhado, o arquivo fica menor e mais limpo.
    amostra = Image.new("RGB", (imagens[0].width, imagens[0].height * len(prontos)))
    passo_real = [i for i, d in zip(imagens, duracoes) if d != DURACAO_DA_TRANSICAO]
    for indice, imagem in enumerate(passo_real):
        amostra.paste(imagem, (0, imagem.height * indice))
    paleta = amostra.quantize(colors=cores, method=Image.Quantize.MEDIANCUT)
    indexadas = [i.quantize(palette=paleta, dither=Image.Dither.NONE) for i in imagens]
    indexadas[0].save(destino, save_all=True, append_images=indexadas[1:], duration=duracoes, loop=0, optimize=False, disposal=1)
    return destino.stat().st_size


def gravar_dentro_do_limite(prontos, destino, largura=LARGURA):
    """Tenta paletas cada vez menores até o arquivo caber no limite de tamanho."""
    for cores in (160, 128, 96, 64, 48):
        tamanho = gravar_gif(prontos, destino, largura, cores)
        if tamanho <= LIMITE_EM_BYTES:
            return tamanho, cores
    sys.exit(f"{destino.name} passou de 3 MB mesmo com 48 cores. Encurte o roteiro da cena ou instale o gifsicle para comprimir mais.")


def principal():
    analisador = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    analisador.add_argument("gifs", nargs="*", choices=[[], *GIFS], help="quais GIFs gerar (padrão: todos)")
    argumentos = analisador.parse_args()
    escolhidos = argumentos.gifs or list(GIFS)
    if os.name != "nt" and not (os.environ.get("DISPLAY") or os.environ.get("WAYLAND_DISPLAY")):
        sys.exit("É preciso uma sessão gráfica: o aplicativo abre uma janela real para ser fotografado.")
    subprocess.run(["dotnet", "build", str(PROJETO_DE_TESTES), "--verbosity", "quiet", "--nologo"], check=True)
    DESTINO.mkdir(parents=True, exist_ok=True)
    temporaria = Path(tempfile.mkdtemp(prefix="bankmanager-midia-"))
    try:
        # O arquivo compartilhado passa da cena "enviar" para a cena "receber"; fica na pasta temporária, com um nome neutro.
        arquivo_compartilhado = temporaria / "despesas-compartilhadas.bmshare.json"
        for nome in escolhidos:
            passos = []
            for cena in GIFS[nome]:
                print(f"{nome}: gravando a cena {cena}…", flush=True)
                passos += ler_roteiro(gravar_quadros(cena, temporaria, arquivo_compartilhado))
            prontos = compor(passos)
            tamanho, cores = gravar_dentro_do_limite(prontos, DESTINO / f"{nome}.gif")
            print(f"{nome}.gif: {len(prontos)} passos, {cores} cores, {tamanho / 1024 / 1024:.2f} MB", flush=True)
            if nome == "visao-geral":
                # Versão leve para o topo do README: menor e com menos cores.
                leve = gravar_gif(prontos, DESTINO / "visao-geral-leve.gif", 640, 64)
                print(f"visao-geral-leve.gif: {leve / 1024 / 1024:.2f} MB", flush=True)
    finally:
        shutil.rmtree(temporaria, ignore_errors=True)
    total = sum(arquivo.stat().st_size for arquivo in DESTINO.glob("*.gif"))
    print(f"Total em docs/midia: {total / 1024 / 1024:.2f} MB")


if __name__ == "__main__":
    principal()
