BankManager 2.1: gestão financeira individual e familiar, em português, com dados locais e sem internet.

## Novidades da 2.1

- **Visual novo ("Estilo C"):** arejado e minimalista, em verde e tons neutros, com fundo em degradê, cartões translúcidos e menu lateral mais estreito. Continua com tema **claro, escuro ou automático**.
- **Visão geral redesenhada:** saldo planejado em destaque, **gráfico das despesas dos últimos 6 meses**, cartão "Resumo da casa", metas do mês em cartões, próximos pagamentos e acerto de contas.
- **Últimas movimentações:** nova lista com as rendas do mês e as despesas pagas, das mais recentes para as mais antigas.
- **Previsões e metas:** a página ganhou um submenu com metas por categoria, previsão do próximo mês, despesas recorrentes e acerto de contas. Tudo continua acessível também de onde já estava.
- **Pessoas e rendas** mostra quanto cada pessoa assume das despesas do mês e quanto sobra.
- Ícones por categoria e números com algarismos de mesma largura em todo o aplicativo.

As regras de cálculo e o formato dos dados **não mudaram**: a 2.1 abre os dados da 2.0 e vice-versa.

## Instalar

**Windows x64:** `BankManager-2.1.0-win-x64-setup.exe` (instalador, sem administrador) ou `BankManager-2.1.0-win-x64.zip` (portátil).

**Linux x64:** `BankManager-2.1.0-x86_64.AppImage` (um arquivo; `chmod +x` e abrir) ou `BankManager-2.1.0-linux-x64.tar.gz` (portátil, com `./Instalar-atalho.sh`).

Todos incluem o .NET. O arquivo `LEIA-ME.txt` traz as instruções completas.

## Atualizar

Da 2.0: basta instalar por cima; nada muda nos dados. Da 1.x: seus dados são lidos e atualizados sem perda na primeira vez que a versão 2 salvar, e o arquivo antigo fica como cópia anterior; **exporte um backup antes**, porque o BankManager 1.x não abre dados salvos pela versão 2.

O aplicativo nunca acessa a internet e não tem atualizador próprio: no Windows, rode o instalador novo; no Linux, baixe o AppImage novo (ou use AppImageUpdate).

## Limitações conhecidas

- Instaladores e pacotes **sem assinatura digital**: o Windows pode avisar sobre editor não identificado.
- O visual no **Windows** (fontes, DPI, degradê, sombras, instalador) ainda não foi validado em um Windows real; a CI o testa, mas como aviso.
- Em "Últimas movimentações", a despesa paga aparece na data de **vencimento**: o aplicativo não guarda o dia do pagamento.
- Linux exige X11/XWayland e bibliotecas do sistema; sem ARM nem Alpine/musl. macOS não tem pacote.
- Os dados não são criptografados.

Os arquivos `.sha256` permitem conferir a integridade de cada download.
