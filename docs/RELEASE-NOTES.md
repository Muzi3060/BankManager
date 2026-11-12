BankManager 2.2: gestão financeira individual e familiar, em português, com dados locais e sem internet.

## Novidades da 2.2

- **Compartilhar despesas:** marque algumas despesas, gere um arquivo (`.bmshare.json`) e envie a outra pessoa. Ao importar, as despesas são **adicionadas** às dela, sem substituir nem apagar nada. O assistente mapeia pessoas e categorias, mostra uma prévia e não duplica o que já foi importado. O arquivo nunca leva rendas, metas ou acertos.
- **Data de pagamento:** ao marcar uma despesa como paga, o aplicativo guarda o dia (hoje, ou o que você informar). A lista mostra "Pago em dd/MM" e as **Últimas movimentações** passam a seguir a data do pagamento.
- **Visual novo ("Estilo C"),** desde a 2.1: verde e tons neutros, fundo em degradê, cartões translúcidos, visão geral com saldo em destaque, gráfico dos últimos 6 meses, metas em cartões e últimas movimentações. **Previsões e metas** reúne metas, previsão, recorrentes e acerto de contas.
- Extrato importado em CSV usa a data de cada linha como data do pagamento; a planilha exportada ganhou a coluna "Pago em".

## Antes de atualizar

**Exporte um backup** (Ajustes → Exportar backup). A 2.2 grava os dados em um formato novo (esquema 3). Ela abre e atualiza sem perda os dados das versões 1.x, 2.0 e 2.1, e o arquivo antigo fica como cópia anterior no primeiro salvamento. Mas **as versões 2.0, 2.1 e 1.x não abrem dados já salvos pela 2.2**.

Despesas que já estavam pagas ficam sem data de pagamento (ela não existia): nas telas, vale o vencimento.

## Instalar

**Windows x64:** `BankManager-2.2.0-win-x64-setup.exe` (instalador, sem administrador) ou `BankManager-2.2.0-win-x64.zip` (portátil).

**Linux x64:** `BankManager-2.2.0-x86_64.AppImage` (um arquivo; `chmod +x` e abrir) ou `BankManager-2.2.0-linux-x64.tar.gz` (portátil, com `./Instalar-atalho.sh`).

Todos incluem o .NET. O arquivo `LEIA-ME.txt` traz as instruções completas. O aplicativo nunca acessa a internet e não tem atualizador próprio: no Windows, rode o instalador novo; no Linux, baixe o AppImage novo (ou use AppImageUpdate).

## Limitações conhecidas

- Instaladores e pacotes **sem assinatura digital**: o Windows pode avisar sobre editor não identificado.
- O visual e o instalador no **Windows** ainda não foram validados em um Windows real; a CI os testa, mas como aviso.
- Os dados, os backups e os arquivos de compartilhamento **não são criptografados**: são textos legíveis.
- Na divisão proporcional, uma despesa importada é rateada com as rendas de quem importa, e o resultado pode ser diferente do de quem enviou.
- Linux exige X11/XWayland e bibliotecas do sistema; sem ARM nem Alpine/musl. macOS não tem pacote.

Os arquivos `.sha256` permitem conferir a integridade de cada download.
