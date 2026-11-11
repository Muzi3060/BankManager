BankManager 2.0: gestão financeira individual e familiar, em português, com dados locais e sem internet.

## Novidades

- **Novo visual** minimalista, com tema **claro, escuro ou automático** (segue o sistema), ícones próprios e telas redesenhadas.
- **Despesas recorrentes:** aluguel, internet e assinaturas se repetem todo mês (até 12 meses à frente), com opção de encerrar.
- **Quem pagou e acerto de contas:** "Bruno deve R$ 383,96 a Ana", com registro de acertos e histórico.
- **Várias rendas por pessoa** no mês (salário, extra, 13º).
- **Categorias personalizadas** e **metas mensais por categoria**, com barra de progresso na visão geral.
- **Busca em todos os meses**, ignorando maiúsculas e acentos.
- **Planilhas:** exportar despesas em CSV (abre no Excel) e **importar o extrato do banco** com prévia.
- Atalho **Ctrl+N** para nova despesa e menu **⋯** em cada despesa.

## Instalar

**Windows x64:** `BankManager-2.0.0-win-x64-setup.exe` (instalador, sem administrador) ou `BankManager-2.0.0-win-x64.zip` (portátil).

**Linux x64:** `BankManager-2.0.0-x86_64.AppImage` (um arquivo; `chmod +x` e abrir) ou `BankManager-2.0.0-linux-x64.tar.gz` (portátil, com `./Instalar-atalho.sh`).

Todos incluem o .NET. O arquivo `LEIA-ME.txt` traz as instruções completas.

## Atualizar a partir da 1.x

Seus dados são lidos e atualizados sem perda na primeira vez que a versão 2 salvar; o arquivo antigo fica como cópia anterior. **Exporte um backup antes.** O BankManager 1.x não abre dados salvos pela versão 2.

O aplicativo nunca acessa a internet e não tem atualizador próprio: no Windows, rode o instalador novo; no Linux, baixe o AppImage novo (ou use AppImageUpdate).

## Limitações conhecidas

- Instaladores e pacotes **sem assinatura digital**: o Windows pode avisar sobre editor não identificado.
- O fluxo visual no **Windows** (fontes, DPI, instalador) ainda não foi validado em um Windows real; a CI o testa, mas como aviso.
- Linux exige X11/XWayland e bibliotecas do sistema; sem ARM nem Alpine/musl. macOS não tem pacote.
- Os dados não são criptografados.

Os arquivos `.sha256` permitem conferir a integridade de cada download.
