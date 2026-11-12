# Glossário: nomes antigos (inglês) e novos (português)

O código foi escrito primeiro com nomes em inglês e depois traduzido. Esta lista ajuda a ler commits, PRs e discussões antigas.

Os nomes **dentro dos arquivos de dados** (`financas.json`, `settings.json`, `.bmshare.json`) não mudaram: lá continua `"Expenses"`, `"DueDate"`, `"Paid"`. No código, a ligação entre os dois é o atributo `[JsonPropertyName("...")]` em cada propriedade gravada.

## Pastas e arquivos

| Antes | Agora |
| --- | --- |
| `BankManager/` | `codigo/BankManager/` |
| `BankManager.Core/` | `codigo/BankManager.Nucleo/` (em `Modelos/`, `Regras/`, `Motores/` e `Arquivos/`) |
| `BankManager.Tests/` | `testes/BankManager.Testes/` |
| `BankManager.UiTests/` | `testes/BankManager.TestesDeInterface/` |
| `Pages/` | `Paginas/` (páginas) e `Dialogos/` (diálogos e painéis) |
| `Styles/Tokens.axaml`, `Icons.axaml`, `Controls.axaml` | `Estilos/Cores.axaml`, `Icones.axaml`, `Controles.axaml` |
| `Assets/icon.*` | `Recursos/icone.*` |
| `installer/` | `instalador/` |
| `scripts/package.py`, `installers.py`, `make-icons.sh` | `scripts/empacotar.py`, `instaladores.py`, `gerar-icones.sh` |
| `run.sh` | `executar.sh` |

## Tipos e valores de enum

| Antes | Agora | O que é |
| --- | --- | --- |
| `Allocation` | `Rateio` | tipo |
| `AmountSign` | `SinalDoValor` | tipo |
| `App` | `Aplicativo` | tipo |
| `AppSettings` | `Preferencias` | tipo |
| `BarChart` | `GraficoDeBarras` | tipo |
| `BarPoint` | `PontoDoGrafico` | tipo |
| `BudgetData` | `DadosDoOrcamento` | tipo |
| `BudgetException` | `ErroDeOrcamento` | tipo |
| `BudgetMigrations` | `MigracoesDeDados` | tipo |
| `BudgetRules` | `RegrasDoOrcamento` | tipo |
| `CategoryEngine` | `MotorDeCategorias` | tipo |
| `CategoryGoal` | `MetaDeCategoria` | tipo |
| `ChartPeer` | `PeerDoGrafico` | tipo |
| `CsvExport` | `ExportacaoCsv` | tipo |
| `CsvTable` | `TabelaCsv` | tipo |
| `Dates` | `Datas` | tipo |
| `Debt` | `Divida` | tipo |
| `DueKind` | `TipoDePrazo` | tipo |
| `DueStatus` | `SituacaoDoPrazo` | tipo |
| `Expense` | `Despesa` | tipo |
| `ExpenseItem` | `ItemDaDespesa` | tipo |
| `ExpenseSearch` | `BuscaDeDespesas` | tipo |
| `ExpenseShare` | `CompartilhamentoDeDespesas` | tipo |
| `FinanceEngine` | `MotorFinanceiro` | tipo |
| `Forecast` | `Previsao` | tipo |
| `GoalEngine` | `MotorDeMetas` | tipo |
| `GoalLevel` | `NivelDaMeta` | tipo |
| `GoalProgress` | `ProgressoDaMeta` | tipo |
| `ImportMapping` | `MapeamentoDeImportacao` | tipo |
| `ImportPlan` | `PlanoDeImportacao` | tipo |
| `LocalStore` | `ArmazenamentoLocal` | tipo |
| `MainWindow` | `JanelaPrincipal` | tipo |
| `Money` | `Dinheiro` | tipo |
| `MonthlyIncome` | `RendaMensal` | tipo |
| `MonthTotal` | `TotalDoMes` | tipo |
| `Movement` | `Movimentacao` | tipo |
| `Person` | `Pessoa` | tipo |
| `PersonAction` | `AcaoDaPessoa` | tipo |
| `PersonBalance` | `SaldoDaPessoa` | tipo |
| `PersonChoice` | `EscolhaDaPessoa` | tipo |
| `Program` | `Programa` | tipo |
| `Recurrence` | `Recorrencia` | tipo |
| `RecurrenceEngine` | `MotorDeRecorrencias` | tipo |
| `SettingsStore` | `ArmazenamentoDePreferencias` | tipo |
| `Settlement` | `Acerto` | tipo |
| `SettlementEngine` | `MotorDeAcertos` | tipo |
| `SharedExpense` | `DespesaCompartilhada` | tipo |
| `ShareFile` | `ArquivoDeCompartilhamento` | tipo |
| `ShareLine` | `LinhaDaImportacao` | tipo |
| `ShareLineStatus` | `SituacaoDaLinha` | tipo |
| `ShareMapping` | `MapeamentoDoCompartilhamento` | tipo |
| `ShareResult` | `ResultadoDaImportacao` | tipo |
| `SplitMode` | `ModoDeDivisao` | tipo |
| `StatementImport` | `ImportacaoDeExtrato` | tipo |
| `ThemePreference` | `PreferenciaDeTema` | tipo |
| `Ui` | `Componentes` | tipo |
| `Added` | `Adicionada` | valor de enum |
| `Auto` | `Automatico` | valor de enum |
| `AvailableIncome` | `RendaDisponivel` | valor de enum |
| `Create` | `Criar` | valor de enum |
| `Dark` | `Escuro` | valor de enum |
| `DueSoon` | `VenceEmBreve` | valor de enum |
| `Equal` | `Igual` | valor de enum |
| `Error` | `Erro` | valor de enum |
| `Existing` | `Existente` | valor de enum |
| `Ignore` | `Ignorar` | valor de enum |
| `Light` | `Claro` | valor de enum |
| `Near` | `Perto` | valor de enum |
| `NegativeIsExpense` | `NegativoEDespesa` | valor de enum |
| `Ok` | `Dentro` | valor de enum |
| `Open` | `EmAberto` | valor de enum |
| `Over` | `Acima` | valor de enum |
| `Overdue` | `Atrasada` | valor de enum |
| `Paid` | `Paga` | valor de enum |
| `PositiveIsExpense` | `PositivoEDespesa` | valor de enum |
| `Skipped` | `Pulada` | valor de enum |

## Métodos, propriedades e constantes

| Antes | Agora |
| --- | --- |
| `Add` | `Adicionar` |
| `AllCategories` | `TodasAsCategorias` |
| `Allocate` | `Ratear` |
| `Amount` | `Valor` |
| `AppIcon` | `IconeDoAplicativo` |
| `Apply` | `Aplicar` |
| `ApplyTheme` | `AplicarTema` |
| `AppName` | `NomeDoAplicativo` |
| `AppVersion` | `VersaoDoAplicativo` |
| `AppVersionLabel` | `RotuloDaVersao` |
| `AskPayer` | `PerguntarQuemPagou` |
| `AtomicWrite` | `GravarDeFormaAtomica` |
| `AutoThemeMini` | `MiniaturaDoTemaAutomatico` |
| `Axis` | `Eixo` |
| `AxisLabel` | `RotuloDoEixo` |
| `BackupPath` | `CaminhoDaCopia` |
| `BalancePanel` | `PainelDoSaldo` |
| `Balances` | `Saldos` |
| `BalloonBrush` | `CorDoBalao` |
| `BalloonBrushProperty` | `CorDoBalaoProperty` |
| `BalloonTextBrush` | `CorDoTextoDoBalao` |
| `BalloonTextBrushProperty` | `CorDoTextoDoBalaoProperty` |
| `Banner` | `Faixa` |
| `BarBrush` | `CorDaBarra` |
| `BarBrushProperty` | `CorDaBarraProperty` |
| `BarRect` | `RetanguloDaBarra` |
| `BigMoney` | `ValorGrande` |
| `Bottom` | `Inferior` |
| `Build` | `Montar` |
| `Button` | `Botao` |
| `Capitalize` | `Capitalizar` |
| `Card` | `Cartao` |
| `Categories` | `Categorias` |
| `CategoriesCard` | `CartaoDeCategorias` |
| `Category` | `Categoria` |
| `CategoryIconName` | `NomeDoIconeDaCategoria` |
| `Cell` | `Celula` |
| `ChangeMonth` | `MudarMes` |
| `ChangeTheme` | `MudarTema` |
| `Chip` | `Etiqueta` |
| `Clean` | `Normalizar` |
| `Clear` | `Limpar` |
| `ClearNotice` | `EsconderAviso` |
| `Clone` | `Clonar` |
| `CloseOverlay` | `FecharSobreposicao` |
| `Collapsible` | `Recolhivel` |
| `Columns` | `Colunas` |
| `Combo` | `ListaDeOpcoes` |
| `Commit` | `Gravar` |
| `Confirm` | `Confirmar` |
| `CopyIncome` | `CopiarRendas` |
| `CreateCategories` | `CriarCategorias` |
| `CsvFiles` | `ArquivosCsv` |
| `Culture` | `Cultura` |
| `CurrentFormat` | `FormatoAtual` |
| `CurrentVersion` | `VersaoAtual` |
| `CustomCategories` | `CategoriasProprias` |
| `Dashboard` | `VisaoGeral` |
| `Date` | `Data` |
| `DateFormats` | `FormatosDeData` |
| `DayOfMonth` | `DiaDoMes` |
| `Debts` | `Dividas` |
| `DefaultDirectory` | `PastaPadrao` |
| `Detect` | `Detectar` |
| `Dialog` | `Dialogo` |
| `DirectoryPath` | `CaminhoDaPasta` |
| `Divider` | `Divisoria` |
| `DueDate` | `Vencimento` |
| `EditExpense` | `EditarDespesa` |
| `EditPerson` | `EditarPessoa` |
| `EmptyCard` | `CartaoVazio` |
| `EmptyExpenses` | `DespesasVazias` |
| `End` | `Encerrar` |
| `EndMonth` | `MesFinal` |
| `EqualInsteadOfProportional` | `IgualEmVezDeProporcional` |
| `Exists` | `Existe` |
| `ExpenseColumns` | `ColunasDeDespesa` |
| `ExpenseMenu` | `MenuDaDespesa` |
| `ExpenseRow` | `LinhaDaDespesa` |
| `Expenses` | `Despesas` |
| `ExpensesPage` | `PaginaDeDespesas` |
| `ExpenseSubtitle` | `SubtituloDaDespesa` |
| `ExpenseTable` | `TabelaDeDespesas` |
| `Export` | `Exportar` |
| `ExportCsv` | `ExportarCsv` |
| `Extension` | `Extensao` |
| `Fallback` | `ResponsavelPadrao` |
| `Field` | `Campo` |
| `FileName` | `NomeDoArquivo` |
| `FilePath` | `CaminhoDoArquivo` |
| `FitOverlay` | `AjustarSobreposicao` |
| `Fold` | `Simplificar` |
| `ForecastPage` | `PaginaDePrevisoes` |
| `Forecasts` | `Previsoes` |
| `ForecastTab` | `AbaDePrevisao` |
| `Format` | `Formatar` |
| `FormatVersion` | `VersaoDoFormato` |
| `FriendlyError` | `ErroAmigavel` |
| `Generate` | `Gerar` |
| `GoalCard` | `CartaoDeMeta` |
| `Goals` | `Metas` |
| `GoalsSection` | `SecaoDeMetas` |
| `GoalsTab` | `AbaDeMetas` |
| `GoTo` | `IrPara` |
| `GridBrush` | `CorDaGrade` |
| `GridBrushProperty` | `CorDaGradeProperty` |
| `Guess` | `Adivinhar` |
| `Heading` | `Cabecalho` |
| `Horizon` | `Horizonte` |
| `HorizonMonths` | `MesesDeHorizonte` |
| `Icon` | `Icone` |
| `IconCircle` | `CirculoComIcone` |
| `Import` | `Importar` |
| `ImportAnyway` | `ImportarMesmoAssim` |
| `ImportDuplicates` | `ImportarDuplicadas` |
| `ImportShared` | `ImportarCompartilhadas` |
| `ImportStatement` | `ImportarExtrato` |
| `ImportStatementFromFile` | `ImportarExtratoDoArquivo` |
| `Income` | `Renda` |
| `IncomeLines` | `LinhasDeRenda` |
| `Incomes` | `Rendas` |
| `InlineNotice` | `AvisoNaPagina` |
| `Input` | `CampoDeTexto` |
| `InstallmentCount` | `TotalDeParcelas` |
| `InstallmentGroup` | `GrupoDeParcelas` |
| `InstallmentNumber` | `NumeroDaParcela` |
| `Installments` | `Parcelar` |
| `Items` | `Itens` |
| `JsonOptions` | `OpcoesJson` |
| `Kind` | `Tipo` |
| `Label` | `Rotulo` |
| `LabelBrush` | `CorDoRotulo` |
| `LabelBrushProperty` | `CorDoRotuloProperty` |
| `Left` | `Esquerda` |
| `Level` | `Nivel` |
| `Load` | `Carregar` |
| `LooksLikeBackup` | `PareceBackup` |
| `LooksLikeShare` | `PareceCompartilhamento` |
| `ManageGoals` | `GerenciarMetas` |
| `ManageRecurrences` | `GerenciarRecorrencias` |
| `MaxBytes` | `MaximoDeBytes` |
| `MaxChars` | `MaximoDeCaracteres` |
| `MaxCustom` | `MaximoDeProprias` |
| `MaxExpenses` | `MaximoDeDespesas` |
| `MaxLength` | `TamanhoMaximo` |
| `MaxRows` | `MaximoDeLinhas` |
| `MenuAction` | `AcaoDeMenu` |
| `MinBar` | `BarraMinima` |
| `MiniBar` | `BarraDaMiniatura` |
| `MiniScreen` | `TelaDaMiniatura` |
| `MissingCategories` | `CategoriasQueFaltam` |
| `ModalCard` | `CartaoModal` |
| `ModalFooter` | `RodapeModal` |
| `Month` | `Mes` |
| `MonthlyTotals` | `TotaisMensais` |
| `MovementRows` | `LinhasDeMovimentacao` |
| `MovementsCard` | `CartaoDeMovimentacoes` |
| `Name` | `Nome` |
| `NameOf` | `NomeDe` |
| `NearRatio` | `FracaoDePerto` |
| `Notify` | `Avisar` |
| `Of` | `De` |
| `OpenShareExport` | `AbrirExportacao` |
| `Options` | `Opcoes` |
| `OriginId` | `IdDeOrigem` |
| `OverlayOpen` | `SobreposicaoAberta` |
| `PageHeader` | `CabecalhoDaPagina` |
| `PaidBy` | `PagaPor` |
| `PaidDate` | `DataDoPagamento` |
| `Parse` | `Interpretar` |
| `ParseDate` | `InterpretarData` |
| `Participants` | `Participantes` |
| `PayerId` | `IdDoPagador` |
| `Peek` | `Espiar` |
| `People` | `Pessoas` |
| `PeoplePage` | `PaginaDePessoas` |
| `PersonId` | `IdDaPessoa` |
| `PickShareFile` | `EscolherArquivoCompartilhado` |
| `Plan` | `Planejar` |
| `Plot` | `AreaDoGrafico` |
| `Progress` | `Progresso` |
| `Prompt` | `Perguntar` |
| `Quantity` | `Quantidade` |
| `Read` | `Ler` |
| `ReadFile` | `LerArquivo` |
| `ReceiverId` | `IdDoRecebedor` |
| `RecentMovements` | `MovimentacoesRecentes` |
| `Recover` | `Recuperar` |
| `RecurrenceId` | `IdDaRecorrencia` |
| `RecurrenceMonth` | `MesDaRecorrencia` |
| `RecurrenceRows` | `LinhasDeRecorrencia` |
| `Recurrences` | `Recorrencias` |
| `RecurrencesTab` | `AbaDeRecorrencias` |
| `ReferenceDate` | `DataDeReferencia` |
| `RegisterSettlement` | `RegistrarAcerto` |
| `RejectInternalPath` | `RecusarCaminhoInterno` |
| `Remaining` | `Sobra` |
| `Remove` | `Remover` |
| `RemoveExpense` | `RemoverDespesa` |
| `Rename` | `Renomear` |
| `MainWindow.Render` | `Desenhar` |
| `Row` | `Linha` |
| `Run` | `Executar` |
| `ExpenseSearch.Run` | `Buscar` |
| `Save` | `Salvar` |
| `SectionHeading` | `TituloDeSecao` |
| `Segmented` | `Segmentado` |
| `SelectColumns` | `ColunasDeSelecao` |
| `SelectedBrush` | `CorDaSelecionada` |
| `SelectedBrushProperty` | `CorDaSelecionadaProperty` |
| `Set` | `Definir` |
| `SettingsPage` | `PaginaDeAjustes` |
| `SettlementCard` | `CartaoDeAcerto` |
| `SettlementRows` | `LinhasDeAcerto` |
| `Settlements` | `Acertos` |
| `SettlementTab` | `AbaDeAcerto` |
| `ShareFiles` | `ArquivosDeCompartilhamento` |
| `ShareKind` | `TipoCompartilhamento` |
| `Sharing` | `ValidarDivisao` |
| `ShowError` | `MostrarErro` |
| `ShowOverlay` | `MostrarSobreposicao` |
| `ShowSettlements` | `MostrarAcertos` |
| `SkippedMonths` | `MesesPulados` |
| `SoonDays` | `DiasDeAviso` |
| `Split` | `Divisao` |
| `Stack` | `Pilha` |
| `StartMonth` | `MesInicial` |
| `StartSelecting` | `IniciarSelecao` |
| `StatusChip` | `EtiquetaDeSituacao` |
| `StepIndicator` | `IndicadorDePassos` |
| `StopSelecting` | `EncerrarSelecao` |
| `Suggest` | `Sugerir` |
| `SummaryLine` | `LinhaDeResumo` |
| `SummaryPanel` | `PainelDeResumo` |
| `TestOpenPath` | `CaminhoDeTesteParaAbrir` |
| `TestSavePath` | `CaminhoDeTesteParaSalvar` |
| `Text` | `Texto` |
| `Theme` | `Tema` |
| `ThemeCard` | `CartaoDeTema` |
| `ThemeMini` | `MiniaturaDoTema` |
| `Title` | `Titulo` |
| `ToCurrent` | `ParaVersaoAtual` |
| `TogglePaid` | `AlternarPagamento` |
| `Top` | `Superior` |
| `TopUpRecurrences` | `CompletarRecorrencias` |
| `TryParseAmount` | `TentarInterpretarValor` |
| `TryParseDate` | `TentarInterpretarData` |
| `UnitPrice` | `PrecoUnitario` |
| `UpcomingCard` | `CartaoDeProximos` |
| `UpcomingChip` | `EtiquetaDePrazo` |
| `UpdateSelectionBar` | `AtualizarBarraDeSelecao` |
| `Validate` | `Validar` |
| `Version` | `Versao` |
| `WithClass` | `ComClasse` |

## Estilos

As classes de estilo e os nomes das cores também foram traduzidos. Exemplos: `card` virou `cartao`, `muted` virou `apagado`, `primary` virou `primario`; `SurfaceBrush` virou `CorDaSuperficie`, `PrimaryBrush` virou `CorPrimaria`, `CardShadow` virou `SombraDoCartao`. Os ícones ficam em `Estilos/Icones.axaml` com nomes como `Icone.Casa` e `Icone.Lixeira`.
