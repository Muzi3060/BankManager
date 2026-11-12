# Resumo da evolução do BankManager (1.0 → 2.2)

Este documento conta, em linguagem simples, tudo o que foi feito desde a versão 1.0: o que mudou, por quê, como foi verificado e o que ainda precisa de atenção. As fases 1 a 4 formam a versão 2.0; as fases 5 e 6, as versões 2.1 e 2.2.

## Em uma frase

O BankManager ganhou um visual novo (temas claro, escuro e automático), sete sistemas novos (recorrências, quem pagou, metas, categorias próprias, várias rendas, busca geral e CSV), instaladores para Windows e Linux, depois um segundo redesenho ("Estilo C"), a data de pagamento das despesas e o compartilhamento de despesas entre pessoas, sem nunca acessar a internet.

## Fase 1 — Fundação visual

| O que | Como ficou |
| --- | --- |
| Temas | Todas as cores estão em `BankManager/Styles/Tokens.axaml`, uma versão para claro e outra para escuro. O código C# não tem nenhuma cor escrita. Trocar uma cor é editar um arquivo só. |
| Preferência de tema | Fica em `settings.json`, separado do `financas.json`. Mudar o tema nunca mexe nos dados financeiros. Se o arquivo estiver corrompido, o app volta ao padrão (Automático). |
| Ícones | Vetoriais, em `Styles/Icons.axaml`, iguais no Windows e no Linux (antes eram caracteres de texto). Há também um ícone do aplicativo. |
| Código | `MainWindow.cs` foi dividido em um arquivo por página, na pasta `Pages/`. |
| Fonte | A fonte Inter já estava no projeto, mas nunca era usada (um estilo a sobrescrevia). Foi corrigido. |
| README | Um caminho pessoal de pasta saiu do repositório público. |

**MVVM:** não foi adotado. Reescreveria todas as telas sem ganho visível, e as regras de cálculo já estão separadas no Core. A divisão em `Pages/` prepara o terreno se um dia for necessário.

## Fase 2 — Novo visual (versão 2.0; substituído na 2.1 pelo "Estilo C", veja a fase 5)

Seguiu os seus protótipos e as cores do documento (todos os pares texto/fundo têm contraste de pelo menos 4,5:1).

- Menu lateral de 232 px, cabeçalho em cada página, aviso no topo, atalho **Ctrl+N** para nova despesa.
- **Visão geral:** faixa de indicadores, "Para onde vai o dinheiro", "Divisão por pessoa" e "Próximos pagamentos" com chip de prazo.
- **Despesas:** lista em linhas, chips (Pago, Em aberto, Vence em N dias, Em atraso), filtro segmentado, busca e menu "⋯" em cada linha.
- **Pessoas, Previsões e Ajustes** redesenhadas; Ajustes ganhou cartões de tema com miniatura e ajuda que abre e fecha.
- Estados vazios amigáveis e mensagens de erro em linguagem simples.

## Fase 3 — Novos sistemas

Todos usam o esquema de dados **versão 2** (a 2.2 passou para a versão 3; veja a fase 6). Arquivos da versão 1 abrem e são atualizados sem perda; o arquivo antigo vira a cópia anterior no primeiro salvamento. Um arquivo de versão maior que a do app é recusado.

| # | Sistema | Resumo |
| --- | --- | --- |
| 1 | **Despesas recorrentes** | Marque "Repetir todo mês": o app cria a despesa em cada mês, até 12 meses à frente, sem duplicar. Dia 31 vence no último dia dos meses curtos. Cada mês é independente (pagar, editar, excluir). "Recorrentes" permite encerrar (mantém o que já foi pago) ou remover. |
| 2 | **Quem pagou e acerto de contas** | Ao marcar como paga, o app pergunta quem pagou. O cartão "Acerto de contas" mostra quem deve a quem (saldo acumulado, com compensação entre os dois sentidos). "Registrar acerto" abate a dívida; há um histórico. |
| 3 | **Metas por categoria** | Uma meta mensal fixa por categoria. A barra fica âmbar a partir de 80% e vermelha acima de 100%. |
| 4 | **Várias rendas por pessoa** | Salário, extra, 13º: cada pessoa pode ter várias linhas de renda no mês. A renda do mês é a soma. |
| 5 | **Categorias próprias** | As 8 padrão são fixas; você cria as suas (até 40). Renomear atualiza despesas, recorrências e metas. Excluir só se não estiver em uso. |
| 6 | **Busca em todos os meses** | Seletor "Este mês / Todos os meses". Ignora maiúsculas e acentos ("saude" acha "Saúde"). |
| 7 | **CSV** | Exportar despesas (abre direto no Excel em português; textos que parecem fórmula são neutralizados). Importar extrato do banco com assistente: colunas sugeridas, prévia ao vivo, linhas com erro apontadas e importação repetida sem duplicar. As despesas importadas entram já pagas, na categoria Outros, sem "quem pagou". |

**Decisões minhas:** meta fixa para todos os meses; saldo do acerto acumulado e pergunta "Quem pagou?" sempre; sem "editar valor daqui em diante" nas recorrências (encerre e crie outra).

## Fase 4 — Windows e Linux

- **Windows:** instalador (Inno Setup) por usuário, sem administrador, com atalho no menu Iniciar e atualização por cima. O desinstalador não apaga os dados.
- **Linux:** AppImage (um arquivo só) e `Instalar-atalho.sh` no pacote portátil, que cria o ícone no menu sem sudo.
- Os pacotes portáteis (`.zip` e `.tar.gz`) continuam existindo.
- **CI** (`.github/workflows/release.yml`): gera tudo, roda os testes de interface também no Windows (como aviso) e faz um teste de fumaça de cada instalador.
- **Versão:** 2.0.0 nesta fase (hoje 2.2.0).
- **Sem Velopack:** o atualizador dele faria o app consultar a internet, contra a regra de funcionar 100% offline. No Windows, o instalador novo atualiza por cima; no Linux, baixa-se o AppImage novo (ou usa-se `AppImageUpdate`). Flatpak ficou de fora por ser pesado para o ganho.

## Fase 5 — "Estilo C" (versão 2.1)

O visual inteiro foi trocado pela direção aprovada no protótipo: sai o roxo, entram verde e tons neutros.

- **Aparência:** fundo em degradê bem claro, cartões translúcidos (branco com transparência, sem depender de efeitos do sistema), cartões brancos para as metas e um cartão escuro "Resumo da casa". Números com algarismos de mesma largura em todo o aplicativo.
- **Menu e cabeçalho:** menu de 188 px com a marca em texto e "Ajustes" no rodapé. O mês fica em uma pílula; clicar no nome do mês volta ao mês atual (ele tem cara de botão, dica e nome acessível).
- **Visão geral:** saldo planejado em destaque, gráfico das despesas dos últimos 6 meses (desenhado pelo próprio aplicativo, com eixo começando em zero), metas do mês em cartões, próximos pagamentos, acerto de contas e a nova lista **Últimas movimentações**.
- **Previsões e metas:** um submenu reúne metas por categoria, previsão do próximo mês, despesas recorrentes e acerto de contas. Nada saiu do lugar antigo: os atalhos continuam em Despesas e na Visão geral.
- **Pessoas e rendas** mostra quanto cada pessoa assume das despesas e quanto sobra (a antiga "Divisão por pessoa").

**Diferenças conscientes em relação ao protótipo:** o eixo do gráfico começa em zero (no protótipo começa em R$ 3 mil, o que exagera diferenças) e o texto do acerto diz "saldo acumulado de todos os meses, já descontados os acertos", que é o que o cálculo faz.

## Fase 6 — Data de pagamento e compartilhamento (versão 2.2)

| O que | Como ficou |
| --- | --- |
| **Data de pagamento** | Marcar como paga guarda o dia (hoje, ou o que você digitar no diálogo "Quem pagou?" ou no formulário). Reabrir apaga. A lista mostra "Pago em dd/MM". |
| **Últimas movimentações** | Passam a usar a data do pagamento: a despesa aparece no mês em que foi paga. A linha esmaecida foi removida; todas têm opacidade total. |
| **Esquema versão 3** | Arquivos das versões 1 e 2 abrem e são atualizados em cadeia, sem perda; o arquivo antigo vira a cópia anterior. Despesas já pagas ficam **sem** data de pagamento (o app não inventa uma): vale o vencimento. |
| **Compartilhar despesas** | Você marca algumas despesas e gera um arquivo `.bmshare.json`. É outro tipo de arquivo, diferente do backup: leva só as despesas marcadas, com as pessoas pelo nome. Nunca leva rendas, metas ou acertos. |
| **Importar sem substituir** | Quem recebe passa por um assistente: confere o arquivo, diz quem é cada pessoa (existente, nova ou ignorar), decide as categorias que faltam e vê a prévia. As despesas são **adicionadas**; nada é apagado. Importar o mesmo arquivo duas vezes não duplica. Tudo é gravado de uma vez, ou nada. |

**Decisões minhas nesta fase:** a seleção para compartilhar fica em uma lista com caixas dentro do assistente (não na lista principal de despesas); o assistente de importação mostra os passos numerados em uma só janela, com a prévia ao vivo; despesas recorrentes viajam como despesas comuns; na divisão proporcional, quem importa escolhe entre manter o modo (recalculado com as rendas de lá) ou dividir igualmente.

## Como foi verificado

- Build sem avisos.
- **95 testes de domínio** (cálculos, validação, recorrências, acerto, CSV, migração com arquivos reais das versões 1 e 2, data de pagamento, compartilhamento: seleção parcial, mapeamentos, duplicatas, parcelas, tipo errado de arquivo, arquivo adulterado e gravação tudo-ou-nada).
- **54 verificações de interface** com a janela real (cadastro, pagamento com data, acerto, recorrência, categorias, metas, abas, importação de extrato, tema, atalho e o fluxo completo exportar → importar despesas em uma base com dados).
- Contraste de pelo menos 4,5:1 entre texto e fundo nos dois temas, conferido por script.
- Capturas com dados de demonstração (fictícios) comparadas com o protótipo.

## O que ainda não foi testado

- **Windows de verdade:** fontes, DPI, degradê, sombras, instalador e desinstalador. A CI exercita, mas como aviso.
- **Seletores de arquivo do sistema** (salvar e abrir): os testes entregam o caminho pronto, sem abrir a janela nativa.
- **Mouse de verdade:** o balão do gráfico ao passar o mouse e os menus "⋯" abertos; os testes disparam os eventos direto.
- **Dois computadores:** o compartilhamento foi testado na mesma máquina, simulando o arquivo de outra pessoa.
- Leitores de tela e navegação completa por teclado; três ou mais pessoas no cartão de acerto (o cálculo é testado).

## Efeitos que merecem atenção

- **As versões 2.0 e 2.1 (e a 1.x) não abrem dados salvos pela 2.2.** Exporte um backup antes de atualizar.
- Editar a renda de um mês antigo muda o que foi devido naquele mês (o rateio proporcional depende da renda). Excluir uma despesa já paga também muda o saldo do acerto.
- Uma despesa importada com divisão proporcional é rateada com as rendas de quem importa: o resultado pode ser diferente do de quem enviou.
- O saldo do mês e o gráfico seguem o **vencimento**; só as últimas movimentações seguem a data do pagamento.
- Mudar uma meta muda a leitura dos meses passados.
- Instaladores e pacotes não são assinados digitalmente; o Windows pode avisar "editor não identificado".
- Os dados, os backups e os arquivos de compartilhamento continuam sem criptografia (senha opcional está no backlog, junto com metas de poupança).

## Para onde olhar no código

| Assunto | Onde |
| --- | --- |
| Regras e cálculos (sem interface) | `BankManager.Core/` |
| Telas | `BankManager/Pages/` |
| Cores, ícones e estilos | `BankManager/Styles/` |
| Componentes (chip, avatar, botões...) | `BankManager/Ui.cs` |
| Testes | `BankManager.Tests/` (domínio) e `BankManager.UiTests/` (janela real) |
| Gráfico da visão geral | `BankManager/Controls/BarChart.cs` |
| Compartilhamento de despesas | `BankManager.Core/ExpenseShare.cs` e `BankManager/Pages/ShareDialogs.cs` |
| Empacotamento | `scripts/package.py`, `scripts/installers.py`, `installer/` |
| Explicações mais detalhadas | `README.md` e `docs/GUIA-DO-CODIGO.md` |

## Próximos passos sugeridos

1. Conferir o resultado da CI no Pull Request e os arquivos gerados.
2. Testar o instalador e o visual em um Windows real antes de divulgar.
3. Só então: merge na `main`, tag `v2.2.0` e conferência da página de Releases (passo a passo no README).
