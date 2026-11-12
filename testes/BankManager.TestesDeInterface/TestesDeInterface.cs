using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using BankManager;
using BankManager.Nucleo;

// Integração com janela real: usa o mesmo App da produção, mas força uma base temporária isolada.
var diretorio = Path.Combine(Path.GetTempPath(), "bankmanager-ui-" + Guid.NewGuid());
Environment.SetEnvironmentVariable("BANKMANAGER_DATA_DIR", diretorio);
// Modo de capturas para a documentação: BANKMANAGER_SCREENSHOTS=<pasta> semeia dados de demonstração (sintéticos),
// fotografa cada página nos temas claro e escuro e encerra, sem executar o roteiro de verificação.
var pastaDeCapturas = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOTS");
if (!string.IsNullOrEmpty(pastaDeCapturas))
{
    using var semente = new ArmazenamentoLocal(diretorio); semente.Salvar(DadosDeDemonstracao());
}
// Dados fictícios que reproduzem o desenho do aplicativo: duas pessoas, dez despesas no mês atual e cinco meses de histórico para o gráfico.
static DadosDoOrcamento DadosDeDemonstracao()
{
    var primeiro = Datas.Mes(DateOnly.FromDateTime(DateTime.Today));
    var ana = new Pessoa { Nome = "Ana" }; var bruno = new Pessoa { Nome = "Bruno" };
    var dados = new DadosDoOrcamento { Pessoas = [ana, bruno], Rendas = [new() { IdDaPessoa = ana.Id, Mes = primeiro, Valor = 4200 }, new() { IdDaPessoa = bruno.Id, Mes = primeiro, Valor = 2800 }] };
    var grupo = Guid.NewGuid();
    void Adicionar(string titulo, string categoria, int dia, decimal valor, bool paga = false, int parcela = 1, int mesesAtras = 0) => dados.Despesas.Add(new Despesa
    {
        Titulo = titulo, Categoria = categoria, Vencimento = primeiro.AddMonths(-mesesAtras).AddDays(dia - 1), Valor = valor, Paga = paga, PagaPor = mesesAtras > 0 ? ana.Id : null, DataDoPagamento = paga ? primeiro.AddMonths(-mesesAtras).AddDays(dia - 1) : null, Divisao = ModoDeDivisao.RendaDisponivel, Participantes = [ana.Id, bruno.Id],
        GrupoDeParcelas = parcela > 1 ? grupo : null, NumeroDaParcela = parcela, TotalDeParcelas = parcela > 1 ? 10 : 1
    });
    Adicionar("Aluguel", "Moradia", 5, 1800, true); Adicionar("Plano de saúde", "Saúde", 10, 640, true); Adicionar("Energia elétrica", "Moradia", 12, 214.90m);
    // Notebook em 10 parcelas (a do mês atual é a terceira); "Assinaturas" é uma categoria criada pelo usuário.
    for (var parte = 1; parte <= 10; parte++) Adicionar("Notebook", "Educação", 15, 389, parte < 3, parcela: parte, mesesAtras: 3 - parte);
    dados.CategoriasProprias.Add("Assinaturas");
    Adicionar("Transporte", "Transporte", 18, 380); Adicionar("Streaming", "Assinaturas", 22, 55.90m); Adicionar("Mercado do mês", "Mercado", 30, 920.40m);
    Adicionar("Padaria", "Mercado", 3, 38.50m, true); Adicionar("Corrida de aplicativo", "Transporte", 6, 42.30m, true);
    // Histórico dos cinco meses anteriores (já pago e acertado), para o gráfico e a previsão terem o que mostrar.
    decimal[] mercado = [870, 990, 850, 1120, 780], energia = [198.40m, 231.70m, 188.20m, 240.10m, 176.90m];
    for (var atras = 1; atras <= 5; atras++)
    {
        Adicionar("Aluguel", "Moradia", 5, 1800, true, mesesAtras: atras); Adicionar("Plano de saúde", "Saúde", 10, 640, true, mesesAtras: atras); Adicionar("Energia elétrica", "Moradia", 12, energia[atras - 1], true, mesesAtras: atras);
        Adicionar("Mercado do mês", "Mercado", 28, mercado[atras - 1], true, mesesAtras: atras); Adicionar("Transporte", "Transporte", 18, 300 + 20 * atras, true, mesesAtras: atras);
        dados.Rendas.Add(new() { IdDaPessoa = ana.Id, Mes = primeiro.AddMonths(-atras), Valor = 4200 }); dados.Rendas.Add(new() { IdDaPessoa = bruno.Id, Mes = primeiro.AddMonths(-atras), Valor = 2800 });
    }
    // A internet é uma despesa mensal desde dois meses atrás; a do mês atual já está paga.
    dados.Recorrencias.Add(new Recorrencia { Titulo = "Internet", Categoria = "Serviços", Valor = 119.90m, DiaDoMes = 8, Divisao = ModoDeDivisao.RendaDisponivel, Participantes = [ana.Id, bruno.Id], MesInicial = primeiro.AddMonths(-2) });
    MotorDeRecorrencias.Gerar(dados, DateOnly.FromDateTime(DateTime.Today)); dados.Despesas.First(e => e.MesDaRecorrencia == primeiro).Paga = true; dados.Despesas.First(e => e.MesDaRecorrencia == primeiro).DataDoPagamento = primeiro.AddDays(7);
    // Metas: Mercado perto do limite (87%), Saúde acima (106%) e Assinaturas dentro (55%).
    MotorDeMetas.Definir(dados, "Mercado", 1100); MotorDeMetas.Definir(dados, "Saúde", 600); MotorDeMetas.Definir(dados, "Assinaturas", 100);
    // Quem pagou as contas já quitadas: o cartão de acerto mostra o saldo entre Ana e Bruno.
    dados.Despesas.First(e => e.MesDaRecorrencia == primeiro).PagaPor = ana.Id; dados.Despesas.First(e => e.Titulo == "Aluguel" && e.Vencimento >= primeiro).PagaPor = ana.Id; dados.Despesas.First(e => e.Titulo == "Plano de saúde" && e.Vencimento >= primeiro).PagaPor = bruno.Id;
    foreach (var e in dados.Despesas.Where(e => e.Vencimento >= primeiro && e.Paga && e.PagaPor is null)) e.PagaPor = ana.Id;
    // O histórico pago por Ana já foi acertado: um acerto no valor exato zera a dívida dos meses anteriores.
    var passado = ArmazenamentoLocal.Clonar(dados); passado.Despesas.RemoveAll(e => e.Vencimento >= primeiro);
    foreach (var divida in MotorDeAcertos.Dividas(passado)) dados.Acertos.Add(new Acerto { IdDoPagador = divida.IdDoDevedor, IdDoRecebedor = divida.IdDoCredor, Valor = divida.Valor, Data = primeiro.AddDays(-1) });
    return dados;
}
// Assume falha até completar todo o roteiro; o CI reconhece o sucesso pelo código de saída.
int codigoDeSaida = 1;
AppBuilder.Configure<Aplicativo>().UsePlatformDetect().WithInterFont().AfterSetup(construtor =>
{
    var cicloDeVida = (IClassicDesktopStyleApplicationLifetime)construtor.Instance!.ApplicationLifetime!;
    // Aguarda a inicialização e executa na thread gráfica, onde os controles Avalonia podem ser manipulados.
    cicloDeVida.Startup += (_, _) => DispatcherTimer.RunOnce(async () =>
    {
        try
        {
            var principal = cicloDeVida.MainWindow!;
            // O último elemento é o diálogo modal quando há um editor aberto; caso contrário, é a janela principal.
            Window Ativa() => cicloDeVida.Windows.Last();
            // Procura o botão no conteúdo visual renderizado pelo rótulo que o usuário lê. Botões com ícone guardam o
            // rótulo no nome de automação (o mesmo que um leitor de tela anuncia), então um único critério serve aos dois tipos.
            Button AcharBotao(Window janela, string rotulo) => janela.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == rotulo);
            Button AcharBotaoEm(Visual escopo, string rotulo) => escopo.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == rotulo);
            // Dispara o evento do botão e dá tempo para atualizar layout e abrir/fechar diálogos.
            // Não simula coordenadas do mouse, seletores nativos de arquivos ou tecnologias assistivas.
            async Task Clicar(string rotulo, Window? janela = null)
            {
                AcharBotao(janela ?? Ativa(), rotulo).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(180);
            }
            // Aciona um item do menu "⋯" da primeira linha visível (o popup em si não é simulado).
            async Task Menu(string rotulo)
            {
                var mais = principal.GetVisualDescendants().OfType<Button>().First(b => AutomationProperties.GetName(b) == "Mais ações");
                ((MenuFlyout)mais.Flyout!).Items.OfType<MenuItem>().First(m => AutomationProperties.GetName(m) == rotulo).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(180);
            }
            // Uma condição falsa interrompe o roteiro e mantém o resultado como falha.
            void Verificar(bool ok, string descricao) { if (!ok) throw new Exception(descricao); Console.WriteLine("PASSOU " + descricao); }
            Verificar(principal.IsVisible, "A janela principal abre");
            // Fotografa uma janela no tamanho em que ela está na tela e salva na pasta de capturas.
            void Captura(Window janela, string nome)
            {
                using var captura = new RenderTargetBitmap(new PixelSize((int)janela.Bounds.Width, (int)janela.Bounds.Height), new Vector(96, 96));
                captura.Render(janela); captura.Save(Path.Combine(pastaDeCapturas!, nome + ".png"), PngBitmapEncoderOptions.Default); Console.WriteLine("Captura: " + nome);
            }
            if (!string.IsNullOrEmpty(pastaDeCapturas))
            {
                foreach (var (tema, nomeDoTema) in new[] { (PreferenciaDeTema.Claro, "claro"), (PreferenciaDeTema.Escuro, "escuro") })
                {
                    Aplicativo.AplicarTema(tema);
                    foreach (var (rotulo, arquivo) in new[] { ("Visão geral", "visao-geral"), ("Despesas", "despesas"), ("Pessoas e rendas", "pessoas"), ("Previsões e metas", "previsoes"), ("Ajustes", "ajustes") })
                    {
                        await Clicar(rotulo, principal); await Task.Delay(300);
                        Captura(principal, $"{arquivo}-{nomeDoTema}");
                        // A visão geral é mais longa que a janela: uma segunda captura mostra o fim da página (acerto e movimentações).
                        if (arquivo != "visao-geral") continue;
                        var rolador = principal.GetVisualDescendants().OfType<ScrollViewer>().First();
                        rolador.Offset = new Vector(0, rolador.Extent.Height - rolador.Viewport.Height); await Task.Delay(300);
                        Captura(principal, $"{arquivo}-2-{nomeDoTema}"); rolador.Offset = default;
                    }
                }
                // As outras abas de "Previsões e metas" (a captura "previsoes" mostra as metas), no tema claro.
                Aplicativo.AplicarTema(PreferenciaDeTema.Claro); await Clicar("Previsões e metas", principal);
                foreach (var (rotulo, arquivo) in new[] { ("Previsão do próximo mês", "previsao"), ("Despesas recorrentes", "recorrentes-aba"), ("Acerto de contas", "acerto") }) { await Clicar(rotulo, principal); await Task.Delay(300); Captura(principal, arquivo); }
                await Clicar("Metas por categoria", principal);
                // Diálogo de recorrentes, que mostra o estado com a internet mensal.
                await Clicar("Despesas", principal); await Clicar("Recorrentes", principal); await Task.Delay(250);
                Captura(Ativa(), "recorrentes");
                await Clicar("Fechar");
                // Fluxo de compartilhar: modo de seleção com três despesas marcadas e o diálogo de exportar.
                Aplicativo.AplicarTema(PreferenciaDeTema.Claro); await Clicar("Despesas", principal); await Clicar("Compartilhar", principal);
                foreach (var nome in new[] { "Notebook", "Transporte", "Streaming" }) principal.GetVisualDescendants().OfType<CheckBox>().First(c => AutomationProperties.GetName(c) == "Selecionar " + nome).IsChecked = true;
                await Task.Delay(250); Captura(principal, "compartilhar-selecionar");
                await Clicar("Compartilhar…", principal); await Task.Delay(300); Captura(principal, "compartilhar-exportar");
                // Salva o arquivo e monta o "arquivo recebido": Carlos no lugar de Bruno, duas despesas novas (uma em categoria que não existe aqui) e uma repetida.
                var janelaDeDemonstracao = (JanelaPrincipal)principal; var pastaDeDemonstracao = Path.Combine(Path.GetTempPath(), "bankmanager-share-" + Guid.NewGuid()); Directory.CreateDirectory(pastaDeDemonstracao);
                janelaDeDemonstracao.CaminhoDeTesteParaSalvar = Path.Combine(pastaDeDemonstracao, "enviado" + ArquivoDeCompartilhamento.Extensao);
                principal.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("modal")).GetVisualDescendants().OfType<Button>().First(x => AutomationProperties.GetName(x) == "Salvar arquivo").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300);
                var arquivoRecebido = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(janelaDeDemonstracao.CaminhoDeTesteParaSalvar).Replace("\"Bruno\"", "\"Carlos\""))!;
                foreach (var item in arquivoRecebido["Expenses"]!.AsArray()) if ((string)item!["Title"]! != "Streaming") item["OriginId"] = Guid.NewGuid().ToString();
                foreach (var item in arquivoRecebido["Expenses"]!.AsArray()) if ((string)item!["Title"]! == "Transporte") item["Category"] = "Viagens";
                arquivoRecebido["CustomCategories"]!.AsArray().Add("Viagens");
                janelaDeDemonstracao.CaminhoDeTesteParaAbrir = Path.Combine(pastaDeDemonstracao, "despesas-nov-2026" + ArquivoDeCompartilhamento.Extensao); File.WriteAllText(janelaDeDemonstracao.CaminhoDeTesteParaAbrir, arquivoRecebido.ToJsonString());
                async Task NoModal(string rotulo) { principal.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("modal")).GetVisualDescendants().OfType<Button>().First(x => AutomationProperties.GetName(x) == rotulo).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(300); }
                await Clicar("Ajustes", principal); await Clicar("Importar despesas compartilhadas", principal); await Task.Delay(350); Captura(principal, "importar-1-arquivo");
                await NoModal("Continuar"); Captura(principal, "importar-2-pessoas"); await NoModal("Continuar"); Captura(principal, "importar-3-previa");
                await NoModal("Adicionar 2 despesas"); Captura(principal, "importar-4-resultado"); await NoModal("Fechar");
                janelaDeDemonstracao.CaminhoDeTesteParaAbrir = null; janelaDeDemonstracao.CaminhoDeTesteParaSalvar = null; try { Directory.Delete(pastaDeDemonstracao, true); } catch (IOException) { }
                // Assistente de importação de extrato, com um arquivo de demonstração (entradas, saídas e uma linha com erro).
                var demonstracao = Path.Combine(diretorio, "extrato-demo.csv");
                File.WriteAllText(demonstracao, "Data;Histórico;Valor\r\n02/11/2026;PIX PADARIA SAO JORGE;-12,50\r\n03/11/2026;SALARIO EMPRESA LTDA;3000,00\r\n04/11/2026;FARMACIA CENTRAL;-87,30\r\n05/11/2026;POSTO IPIRANGA;-210,00\r\n06/11/2026;AMAZON MARKETPLACE;-1.234,56\r\nontem;LINHA RUIM;-1,00\r\n", new System.Text.UTF8Encoding(true));
                _ = ((JanelaPrincipal)principal).ImportarExtratoDoArquivo(demonstracao); await Task.Delay(400);
                Captura(Ativa(), "importar-extrato");
                await Clicar("Cancelar");
                codigoDeSaida = 0; return;
            }
            // O eixo do gráfico começa em zero e usa passos "redondos"; sem dados, não há eixo a desenhar além do zero.
            Verificar(BankManager.Controles.GraficoDeBarras.Eixo(4600.90m) == (6000m, 2000m) && BankManager.Controles.GraficoDeBarras.Eixo(950m) == (1000m, 500m) && BankManager.Controles.GraficoDeBarras.Eixo(0m) == (0m, 0m)
                && BankManager.Controles.GraficoDeBarras.RotuloDoEixo(1500m, Dinheiro.Cultura) == "R$ 1,5 mil" && BankManager.Controles.GraficoDeBarras.RotuloDoEixo(0m, Dinheiro.Cultura) == "R$ 0", "O eixo do gráfico começa em zero, com passos redondos");
            // Cenário 1: cadastro de pessoas e rendas pela interface, incluindo o fechamento do modal ao salvar.
            await Clicar("Cadastrar primeira pessoa", principal);
            var entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray(); entradas.First(i => i.PlaceholderText == "Ex.: Ana").Text = "Ana"; entradas.First(i => i.PlaceholderText == "Ex.: 3000,00").Text = "3000,00";
            await Clicar("Salvar pessoa");
            Verificar(cicloDeVida.Windows.Count == 1, "O diálogo de pessoa salva e fecha");
            await Clicar("Pessoas e rendas", principal); await Clicar("Adicionar pessoa", principal);
            entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray(); entradas.First(i => i.PlaceholderText == "Ex.: Ana").Text = "Bruno"; entradas.First(i => i.PlaceholderText == "Ex.: 3000,00").Text = "1000,00"; await Clicar("Salvar pessoa");
            // Cenário 1b: uma pessoa pode ter várias rendas no mês; a linha "Extra" de zero mantém os totais dos cenários seguintes.
            await Clicar("Editar", principal); await Clicar("Adicionar renda");
            entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray(); entradas.Where(i => i.PlaceholderText == "Ex.: Salário").Last().Text = "Extra"; entradas.Where(i => i.PlaceholderText == "Ex.: 3000,00").Last().Text = "0,00";
            await Clicar("Salvar pessoa");
            var linhasDaAna = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")); Verificar(linhasDaAna.Rendas.Count(i => i.IdDaPessoa == linhasDaAna.Pessoas[0].Id) == 2 && MotorFinanceiro.Renda(linhasDaAna, linhasDaAna.Pessoas[0].Id, Datas.Mes(DateOnly.FromDateTime(DateTime.Today))) == 3000m, "Uma pessoa pode ter várias linhas de renda");
            // Cenário 2: compra compartilhada com prévia proporcional de 600/200 e conferência do JSON salvo.
            await Clicar("Despesas", principal); await Clicar("Nova despesa", principal);
            entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray();
            entradas.First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Mercado da família";
            entradas.First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "800,00";
            await Clicar("Conferir divisão antes de salvar");
            Verificar(Ativa().GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("600,00") == true && t.Text.Contains("200,00")), "A prévia proporcional mostra 600 / 200");
            await Clicar("Salvar despesa");
            Verificar(ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Single().Valor == 800m, "A despesa criada pela interface é gravada");
            // Cenário 3: mudança de situação precisa chegar ao armazenamento, não apenas à tela.
            // Pela visão geral, o item "Marcar como pago" do menu "⋯" dos próximos pagamentos.
            // Marcar como pago pergunta quem pagou: aqui, Ana.
            await Clicar("Visão geral", principal); await Menu("Marcar como pago"); await Clicar("Ana");
            var dadosPagos = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json"));
            Verificar(dadosPagos.Despesas.Single().Paga && dadosPagos.Despesas.Single().PagaPor == dadosPagos.Pessoas[0].Id && dadosPagos.Despesas.Single().DataDoPagamento == DateOnly.FromDateTime(DateTime.Today), "Marcar como pago grava quem pagou e a data de hoje");
            // A visão geral nova: o gráfico tem texto alternativo com os valores e a despesa paga entra nas últimas movimentações, depois das rendas.
            var grafico = principal.GetVisualDescendants().OfType<BankManager.Controles.GraficoDeBarras>().Single();
            Verificar(AutomationProperties.GetName(grafico)!.StartsWith("Despesas dos últimos 6 meses") && AutomationProperties.GetName(grafico)!.Contains(Dinheiro.Formatar(800m)), "O gráfico expõe os valores como texto alternativo");
            var textos = principal.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToList();
            Verificar(textos.Contains("− " + Dinheiro.Formatar(800m)) && textos.Contains("+ " + Dinheiro.Formatar(3000m)) && textos.Contains("+ " + Dinheiro.Formatar(1000m)) && textos.IndexOf("− " + Dinheiro.Formatar(800m)) < textos.IndexOf("+ " + Dinheiro.Formatar(3000m)), "Movimentações recentes listam despesas pagas e rendas, das mais novas para as mais antigas");
            // Nenhuma linha das movimentações fica esmaecida, e o nome do mês é um botão com nome acessível e dica.
            Verificar(principal.GetVisualDescendants().OfType<Grid>().All(g => g.Opacity == 1) && ToolTip.GetTip(AcharBotao(principal, "Voltar ao mês atual")) is "Voltar ao mês atual", "As linhas de movimentação têm opacidade total e o mês é um botão com nome");
            // Ana pagou 800 (600/200): o cartão de acerto mostra que Bruno deve 200 a Ana.
            var textoDaDivida = $"Bruno deve {Dinheiro.Formatar(200m)} a Ana";
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(x => AutomationProperties.GetName(x) == textoDaDivida), "O cartão de acerto mostra quem deve a quem");
            await Clicar("Registrar acerto", principal); await Clicar("Salvar acerto");
            var acertado = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json"));
            Verificar(acertado.Acertos.Count == 1 && acertado.Acertos[0].Valor == 200m && MotorDeAcertos.Dividas(acertado).Count == 0, "Registrar um acerto zera a dívida");
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.StartsWith("Tudo certo") == true), "O cartão de acerto mostra tudo certo");
            // Cenário 4: valor inválido mantém o formulário aberto e preserva o valor anteriormente salvo.
            // A edição fica no menu "⋯" da linha: aciona o item "Editar" sem depender de abrir o popup.
            await Clicar("Despesas", principal); await Menu("Editar");
            entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray(); entradas.First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "-1";
            await Clicar("Salvar despesa");
            Verificar(cicloDeVida.Windows.Count == 2, "Despesa inválida mantém o editor aberto");
            Verificar(ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Single().Valor == 800m, "Edição inválida preserva o valor salvo");
            await Clicar("Cancelar");
            // Cenário 4b: reabrir apaga a data do pagamento; pagar de novo com uma data inválida mantém o diálogo aberto, e uma data
            // válida é gravada e aparece na linha como "Pago em dd/MM".
            await Menu("Marcar em aberto");
            var reaberta = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Single(); Verificar(!reaberta.Paga && reaberta.DataDoPagamento is null && reaberta.PagaPor is null, "Reabrir apaga a data de pagamento");
            await Menu("Marcar como pago");
            var dataDePagar = Ativa().GetVisualDescendants().OfType<TextBox>().First(i => AutomationProperties.GetName(i) == "Data do pagamento (dd/mm/aaaa)");
            dataDePagar.Text = "31/02/2026"; await Clicar("Bruno"); Verificar(cicloDeVida.Windows.Count == 2, "Data de pagamento inválida mantém o diálogo aberto");
            var dataEscolhida = Datas.Mes(DateOnly.FromDateTime(DateTime.Today)).AddDays(1);
            dataDePagar.Text = dataEscolhida.ToString("dd/MM/yyyy"); await Clicar("Ana");
            var pagaDeNovo = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Single(); Verificar(pagaDeNovo.Paga && pagaDeNovo.DataDoPagamento == dataEscolhida, "A data de pagamento escolhida é gravada");
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == $"Pago em {dataEscolhida:dd/MM}"), "A linha da despesa mostra a data de pagamento");
            // Cenário 5: duas unidades de 25,50 devem gerar uma despesa de 51,00 com a quantidade registrada.
            await Clicar("Nova despesa", principal);
            entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray();
            entradas.First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Compra detalhada";
            Ativa().GetVisualDescendants().OfType<Expander>().Single().IsExpanded = true;
            await Task.Delay(180);
            entradas = Ativa().GetVisualDescendants().OfType<TextBox>().ToArray();
            entradas.First(i => i.PlaceholderText == "Nome do item").Text = "Arroz";
            entradas.First(i => i.PlaceholderText == "Quantidade").Text = "2";
            entradas.First(i => i.PlaceholderText == "Preço (R$)").Text = "25,50";
            await Clicar("+ Adicionar item");
            await Clicar("Salvar despesa");
            var despesaComItens = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Single(e => e.Titulo == "Compra detalhada");
            Verificar(despesaComItens.Valor == 51m && despesaComItens.Itens.Single().Quantidade == 2m, "A lista de itens calcula e grava 2 × 25,50");
            // Cenário 6: navegação renderiza as páginas de previsão e backup sem erros.
            // "Previsões e metas" abre nas metas (ainda vazias) e o submenu leva às outras três abas.
            await Clicar("Previsões e metas", principal);
            Verificar(AcharBotao(principal, "Criar primeira meta").IsVisible, "A aba de metas mostra o estado vazio");
            await Clicar("Previsão do próximo mês", principal);
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.StartsWith("Estimativa para") == true), "A aba de previsão aparece");
            await Clicar("Despesas recorrentes", principal);
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Nenhuma despesa recorrente ainda"), "A aba de recorrentes aparece");
            await Clicar("Acerto de contas", principal);
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Histórico de acertos") && principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains("Ana pagou a") == true || t.Text?.Contains("Bruno pagou a Ana") == true), "A aba de acerto mostra o histórico");
            await Clicar("Ajustes", principal);
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Seus dados"), "A tela de ajustes aparece");
            // Cenário 6b: categorias próprias são criadas em Ajustes e ficam disponíveis no editor de despesas.
            Ativa().GetVisualDescendants().OfType<TextBox>().First(i => i.PlaceholderText == "Nova categoria").Text = "Pets"; await Clicar("Adicionar categoria", principal);
            Verificar(ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).CategoriasProprias.SequenceEqual(["Pets"]), "Categoria própria é criada em Ajustes");
            // Cenário 7: o tema muda na hora, vai para settings.json e não toca nas finanças.
            var financasAntes = File.ReadAllBytes(Path.Combine(diretorio, "financas.json"));
            await Clicar("Tema escuro", principal);
            Verificar(Application.Current!.RequestedThemeVariant == ThemeVariant.Dark, "O tema escuro vale na hora");
            // O fundo da janela é um degradê vindo de um DynamicResource: confirma que ele realmente mudou de cor com o tema.
            var fundoEscuro = ((IGradientBrush)principal.Background!).GradientStops[0].Color;
            await Clicar("Tema claro", principal);
            var fundoClaro = ((IGradientBrush)principal.Background!).GradientStops[0].Color;
            Verificar(Application.Current.RequestedThemeVariant == ThemeVariant.Light && fundoEscuro != fundoClaro && fundoClaro.R > fundoEscuro.R, "O fundo da janela acompanha o tema");
            Verificar(ArmazenamentoDePreferencias.Carregar(diretorio).Tema == PreferenciaDeTema.Claro, "A preferência de tema é gravada em settings.json");
            Verificar(financasAntes.SequenceEqual(File.ReadAllBytes(Path.Combine(diretorio, "financas.json"))), "Trocar o tema não toca no financas.json");
            // Cenário 8: Ctrl+N abre o editor de nova despesa de qualquer tela.
            principal.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.N, KeyModifiers = Avalonia.Input.KeyModifiers.Control }); await Task.Delay(300);
            Verificar(cicloDeVida.Windows.Count == 2, "Ctrl+N abre o editor de nova despesa");
            await Clicar("Cancelar");
            // Cenário 9: excluir pelo menu "⋯" pede confirmação; confirmar remove só aquele lançamento.
            await Clicar("Despesas", principal); var quantidadeAntes = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Count;
            await Menu("Excluir"); await Clicar("Confirmar");
            Verificar(ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Count == quantidadeAntes - 1, "Excluir pelo menu da linha remove uma despesa");
            // Cenário 10: despesa que se repete todo mês cria o modelo e os lançamentos até 12 meses à frente; depois é encerrada.
            await Clicar("Nova despesa", principal);
            Ativa().GetVisualDescendants().OfType<TextBox>().First(i => i.PlaceholderText == "Ex.: Compra do mercado").Text = "Aluguel";
            Ativa().GetVisualDescendants().OfType<TextBox>().First(i => i.PlaceholderText == "Ex.: 1000,00").Text = "1500,00";
            Ativa().GetVisualDescendants().OfType<CheckBox>().First(c => AutomationProperties.GetName(c) == "Repetir todo mês").IsChecked = true; await Task.Delay(120);
            await Clicar("Salvar despesa");
            var salvo = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json"));
            Verificar(salvo.Recorrencias.Count == 1 && salvo.Despesas.Count(e => e.IdDaRecorrencia is not null) == MotorDeRecorrencias.MesesDeHorizonte + 1, "Repetir todo mês cria o modelo e 13 meses");
            await Clicar("Recorrentes", principal); await Clicar("Encerrar"); await Clicar("Confirmar");
            salvo = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json"));
            Verificar(salvo.Recorrencias[0].MesFinal == Datas.Mes(DateOnly.FromDateTime(DateTime.Today)) && salvo.Despesas.Count(e => e.IdDaRecorrencia is not null) == 1, "Encerrar uma recorrência mantém só o mês selecionado");
            await Clicar("Fechar");
            // Cenário 10b: a busca vai a todos os meses; um termo que não existe no mês aparece ao ampliar o escopo.
            await Clicar("Despesas", principal); await Clicar("Todos os meses", principal);
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.StartsWith("Busca em todos os meses") == true), "A busca em todos os meses abre");
            await Clicar("Este mês", principal);
            // Cenário 10c: importar um extrato em CSV (o assistente é aberto direto no arquivo, sem o seletor nativo).
            var extrato = Path.Combine(diretorio, "extrato.csv");
            File.WriteAllText(extrato, "Data;Histórico;Valor\r\n02/11/2026;PIX PADARIA;-12,50\r\n03/11/2026;SALARIO;3000,00\r\n04/11/2026;FARMACIA CENTRAL;-1.234,56\r\n", new System.Text.UTF8Encoding(true));
            var antes = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json")).Despesas.Count;
            _ = ((JanelaPrincipal)principal).ImportarExtratoDoArquivo(extrato); await Task.Delay(500);
            Verificar(Ativa().GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.StartsWith("2 despesas serão importadas") == true), "O assistente mostra a prévia do extrato");
            await Clicar("Importar");
            var importado = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json"));
            Verificar(importado.Despesas.Count == antes + 2 && importado.Despesas.Any(e => e.Titulo == "FARMACIA CENTRAL" && e.Valor == 1234.56m && e.Paga && e.PagaPor is null && e.DataDoPagamento == new DateOnly(2026, 11, 4)), "Importar extrato cria despesas pagas");
            // Importar o mesmo arquivo outra vez não repete nada: o botão Importar fica desligado e o aviso mostra as repetidas.
            _ = ((JanelaPrincipal)principal).ImportarExtratoDoArquivo(extrato); await Task.Delay(500);
            Verificar(Ativa().GetVisualDescendants().OfType<TextBlock>().Any(x => x.Text?.Contains("2 linha(s) repetida(s)") == true) && !AcharBotao(Ativa(), "Importar").IsEnabled, "Reimportar o mesmo extrato só encontra duplicadas");
            await Clicar("Cancelar");
            // Cenário 11: meta de Mercado (R$ 500) abaixo do gasto (R$ 800): salva e a barra aparece em alerta vermelho.
            await Clicar("Visão geral", principal); await Clicar("Criar primeira meta", principal);
            Ativa().GetVisualDescendants().OfType<TextBox>().First(i => AutomationProperties.GetName(i) == "Meta de Mercado").Text = "500,00"; await Clicar("Salvar metas");
            var metaSalva = ArmazenamentoLocal.Ler(Path.Combine(diretorio, "financas.json"));
            Verificar(metaSalva.Metas.Count == 1 && metaSalva.Metas[0].Categoria == "Mercado" && metaSalva.Metas[0].Valor == 500m, "A meta é salva pelo diálogo");
            Verificar(principal.GetVisualDescendants().OfType<ProgressBar>().Any(b => b.Classes.Contains("acima")), "Meta acima do limite fica destacada");
            // A mesma meta aparece em lista na aba de metas, com o valor que passou do limite.
            await Clicar("Previsões e metas", principal); await Clicar("Metas por categoria", principal);
            Verificar(principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Acima da meta") && principal.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Mercado · meta " + Dinheiro.Formatar(500m)), "A aba de metas lista o cartão da meta");
            await Clicar("Visão geral", principal);
            // Cenário 12: modo de seleção da página Despesas e diálogo de exportar. O arquivo leva só as despesas marcadas.
            var janelaDoApp = (JanelaPrincipal)principal; var arquivoDeDados = Path.Combine(diretorio, "financas.json");
            var pastaDoCompartilhamento = Path.Combine(Path.GetTempPath(), "bankmanager-share-" + Guid.NewGuid()); Directory.CreateDirectory(pastaDoCompartilhamento); var caminhoDoCompartilhamento = Path.Combine(pastaDoCompartilhamento, "despesas" + ArquivoDeCompartilhamento.Extensao);
            var antesDeCompartilhar = ArmazenamentoLocal.Ler(arquivoDeDados); var bytesAntesDeCompartilhar = File.ReadAllBytes(arquivoDeDados); var mesAtual = Datas.Mes(DateOnly.FromDateTime(DateTime.Today)); var noMes = antesDeCompartilhar.Despesas.Count(e => Datas.Mes(e.Vencimento) == mesAtual);
            // O diálogo sobreposto é um cartão dentro da janela principal; os controles dele são procurados só dentro do cartão.
            Border? Modal() => principal.GetVisualDescendants().OfType<Border>().FirstOrDefault(x => x.Classes.Contains("modal"));
            async Task ClicarEm(string rotulo) { AcharBotaoEm(Modal()!, rotulo).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(220); }
            async Task TeclaEsc() { principal.RaiseEvent(new Avalonia.Input.KeyEventArgs { RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent, Key = Avalonia.Input.Key.Escape }); await Task.Delay(220); }
            bool TemTexto(Visual escopo, Func<string, bool> teste) => escopo.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text is { } texto && teste(texto));
            List<CheckBox> CaixasDasLinhas() => principal.GetVisualDescendants().OfType<CheckBox>().Where(c => AutomationProperties.GetName(c)?.StartsWith("Selecionar ") == true).ToList();
            var barra = principal.GetVisualDescendants().OfType<Border>().First(x => x.Classes.Contains("barra-selecao"));
            await Clicar("Despesas", principal);
            Verificar(!barra.IsVisible && CaixasDasLinhas().Count == 0, "O modo de seleção começa desligado");
            await Clicar("Compartilhar", principal);
            Verificar(barra.IsVisible && CaixasDasLinhas().Count == noMes && !AcharBotao(principal, "Compartilhar…").IsEnabled && TemTexto(principal, t => t == "Escolha as despesas que quer enviar.")
                && !principal.GetVisualDescendants().OfType<Button>().Any(x => AutomationProperties.GetName(x) == "Mais ações"), "O botão Compartilhar liga o modo de seleção, com caixas e sem menus nas linhas");
            await Clicar("Todas deste mês", principal);
            Verificar(TemTexto(barra, t => t == $"{noMes} selecionadas") && principal.GetVisualDescendants().OfType<Border>().Count(x => x.Classes.Contains("linha-despesa") && x.Classes.Contains("selecionado")) == noMes && AcharBotao(principal, "Compartilhar…").IsEnabled, "Todas deste mês marca todas as linhas visíveis");
            await Clicar("Limpar seleção", principal); Verificar(TemTexto(barra, t => t == "0 selecionadas"), "Limpar seleção desmarca tudo");
            // "Uma categoria…" marca só a categoria escolhida entre as visíveis.
            ((MenuFlyout)AcharBotao(principal, "Uma categoria…").Flyout!).Items.OfType<MenuItem>().First(m => AutomationProperties.GetName(m) == "Mercado").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(200);
            Verificar(TemTexto(barra, t => t == $"{antesDeCompartilhar.Despesas.Count(e => Datas.Mes(e.Vencimento) == mesAtual && e.Categoria == "Mercado")} selecionada" || t.EndsWith(" selecionadas") && !t.StartsWith("0 ")), "O atalho de categoria marca aquela categoria");
            // Esc sai do modo e esquece a seleção.
            await TeclaEsc(); Verificar(!barra.IsVisible && CaixasDasLinhas().Count == 0, "Esc sai do modo de seleção");
            // Duas despesas marcadas pela caixa; a busca filtra a lista sem perder a seleção.
            // (as duas despesas divididas entre Ana e Bruno)
            await Clicar("Compartilhar", principal); foreach (var nome in new[] { "Selecionar Mercado da família", "Selecionar Aluguel" }) CaixasDasLinhas().First(c => AutomationProperties.GetName(c) == nome).IsChecked = true; await Task.Delay(150);
            Verificar(TemTexto(barra, t => t == "2 selecionadas") && TemTexto(barra, t => t.StartsWith("Total ")), "As caixas das linhas atualizam a barra flutuante");
            var caixaDeBusca = principal.GetVisualDescendants().OfType<TextBox>().First(t => AutomationProperties.GetName(t) == "Buscar despesas"); caixaDeBusca.Text = "texto que não existe"; await Task.Delay(200);
            Verificar(TemTexto(barra, t => t == "2 selecionadas") && CaixasDasLinhas().Count == 0, "Filtrar mantém a seleção"); caixaDeBusca.Text = ""; await Task.Delay(200);
            Verificar(CaixasDasLinhas().Count(c => c.IsChecked == true) == 2, "A seleção continua depois de limpar o filtro");
            // O diálogo de exportar abre sobre a janela, com o foco no título; Cancelar e Esc fecham e mantêm a seleção.
            await Clicar("Compartilhar…", principal); await Task.Delay(250);
            Verificar(Modal() is { } cartao && AutomationProperties.GetName(cartao) == "Compartilhar despesas" && cicloDeVida.Windows.Count == 1 && AcharBotaoEm(cartao, "Fechar").IsVisible, "O diálogo de exportar abre sobreposto, com botão de fechar");
            Verificar(principal.FocusManager?.GetFocusedElement() is TextBlock focado && focado.Text == "Compartilhar despesas", "O diálogo de exportar leva o foco para o título");
            Verificar(TemTexto(Modal()!, t => t == "2") && TemTexto(Modal()!, t => t.Contains("Envie só para quem deve receber")) && TemTexto(Modal()!, t => t.EndsWith(ArquivoDeCompartilhamento.Extensao)), "O diálogo de exportar mostra os indicadores, o aviso e o nome do arquivo");
            await ClicarEm("Cancelar"); Verificar(Modal() is null && TemTexto(barra, t => t == "2 selecionadas") && !File.Exists(caminhoDoCompartilhamento), "Cancelar fecha o diálogo de exportar e mantém a seleção");
            await Clicar("Compartilhar…", principal); await TeclaEsc(); Verificar(Modal() is null && barra.IsVisible, "Esc fecha o diálogo de exportar");
            await Clicar("Compartilhar…", principal); await ClicarEm("Fechar"); Verificar(Modal() is null, "O botão X fecha o diálogo de exportar");
            // Salvar: o arquivo tem só as duas despesas, em aberto; o modo de seleção termina e o aviso mostra onde o arquivo ficou.
            janelaDoApp.CaminhoDeTesteParaSalvar = caminhoDoCompartilhamento; await Clicar("Compartilhar…", principal); await ClicarEm("Salvar arquivo");
            var compartilhada = CompartilhamentoDeDespesas.Ler(caminhoDoCompartilhamento); var textoDoCompartilhamento = File.ReadAllText(caminhoDoCompartilhamento);
            Verificar(compartilhada.Despesas.Count == 2 && compartilhada.Despesas.All(e => !e.Paga) && compartilhada.VersaoDoAplicativo.Length > 0 && !textoDoCompartilhamento.Contains("Incomes") && !textoDoCompartilhamento.Contains("Goals") && !textoDoCompartilhamento.Contains("Settlements"), "O arquivo tem só as despesas marcadas, em aberto");
            Verificar(Modal() is null && !barra.IsVisible && TemTexto(principal, t => t.Contains(caminhoDoCompartilhamento)) && bytesAntesDeCompartilhar.SequenceEqual(File.ReadAllBytes(arquivoDeDados)), "Salvar confirma o local, sai do modo de seleção e não muda os dados");
            // Cenário 13: assistente "Importar despesas compartilhadas" (Arquivo → Pessoas → Prévia → resultado), em uma base com dados.
            bool FocoEm(string titulo) => principal.FocusManager?.GetFocusedElement() is TextBlock t && t.Text == titulo;
            ComboBox Caixa(string nome) => Modal()!.GetVisualDescendants().OfType<ComboBox>().First(c => AutomationProperties.GetName(c) == nome);
            async Task AbrirImportacao(string arquivo) { janelaDoApp.CaminhoDeTesteParaAbrir = arquivo; await Clicar("Ajustes", principal); var origem = AcharBotao(principal, "Importar despesas compartilhadas"); origem.Focus(); origem.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(350); }
            bool SemMudanca(byte[] bytes) => bytes.SequenceEqual(File.ReadAllBytes(arquivoDeDados));
            // 13a. O próprio arquivo: tudo já existe. Passo 1 valida o arquivo; o foco vai para o título de cada passo.
            await AbrirImportacao(caminhoDoCompartilhamento);
            Verificar(Modal() is not null && FocoEm("Importar despesas compartilhadas") && TemTexto(Modal()!, t => t.StartsWith("Arquivo válido, gerado pelo BankManager")) && AcharBotaoEm(Modal()!, "Continuar").IsEnabled, "O passo 1 da importação valida o arquivo");
            await ClicarEm("Continuar");
            Verificar(FocoEm("Quem é quem?") && Caixa("Pessoa: Ana").SelectedIndex == 0 && TemTexto(Modal()!, t => t == "Mesmo nome"), "O passo 2 da importação sugere as pessoas pelo nome");
            await ClicarEm("Continuar");
            Verificar(FocoEm("Confira antes de adicionar") && TemTexto(Modal()!, t => t == "2 já existem · serão puladas") && !AcharBotaoEm(Modal()!, "Nada a adicionar").IsEnabled, "O passo 3 da importação pula o que já existe");
            var caixaDeForcar = Modal()!.GetVisualDescendants().OfType<CheckBox>().First(c => AutomationProperties.GetName(c)?.StartsWith("Importar mesmo assim: ") == true); var nomeForcado = AutomationProperties.GetName(caixaDeForcar)!;
            caixaDeForcar.IsChecked = true; await Task.Delay(200);
            Verificar(AcharBotaoEm(Modal()!, "Adicionar 1 despesa").IsEnabled && TemTexto(Modal()!, t => t == "1 já existe · será pulada"), "Importar mesmo assim transforma uma duplicada em adição");
            // Voltar até o passo 1 e avançar de novo não perde as escolhas.
            await ClicarEm("Voltar"); await ClicarEm("Voltar"); Verificar(FocoEm("Importar despesas compartilhadas"), "Voltar retorna aos passos anteriores"); await ClicarEm("Continuar"); await ClicarEm("Continuar");
            Verificar(Modal()!.GetVisualDescendants().OfType<CheckBox>().First(c => AutomationProperties.GetName(c) == nomeForcado).IsChecked == true && AcharBotaoEm(Modal()!, "Adicionar 1 despesa").IsEnabled, "Voltar mantém as escolhas");
            // Esc com escolhas feitas pede confirmação; recusar mantém o assistente, confirmar fecha sem gravar e devolve o foco ao botão de origem.
            await TeclaEsc(); Verificar(cicloDeVida.Windows.Count == 2, "Esc pergunta antes de descartar as escolhas"); await Clicar("Cancelar"); Verificar(Modal() is not null, "Recusar mantém o assistente aberto");
            await TeclaEsc(); await Clicar("Confirmar");
            Verificar(Modal() is null && SemMudanca(bytesAntesDeCompartilhar) && principal.FocusManager?.GetFocusedElement() is Button retorno && AutomationProperties.GetName(retorno) == "Importar despesas compartilhadas", "Confirmar fecha o assistente, não grava nada e devolve o foco");
            // Cancelar em cada passo e o X não mudam a base.
            await AbrirImportacao(caminhoDoCompartilhamento); await ClicarEm("Cancelar"); var cancelou1 = Modal() is null && SemMudanca(bytesAntesDeCompartilhar);
            await AbrirImportacao(caminhoDoCompartilhamento); await ClicarEm("Continuar"); await ClicarEm("Cancelar"); var cancelou2 = Modal() is null && SemMudanca(bytesAntesDeCompartilhar);
            await AbrirImportacao(caminhoDoCompartilhamento); await ClicarEm("Continuar"); await ClicarEm("Continuar"); await ClicarEm("Fechar");
            Verificar(cancelou1 && cancelou2 && Modal() is null && SemMudanca(bytesAntesDeCompartilhar), "Cancelar em qualquer passo não altera os dados");
            // Importar mesmo assim, de verdade: uma despesa nova, a outra pulada, e a tela de resultado.
            await AbrirImportacao(caminhoDoCompartilhamento); await ClicarEm("Continuar"); await ClicarEm("Continuar");
            Modal()!.GetVisualDescendants().OfType<CheckBox>().First(c => AutomationProperties.GetName(c) == nomeForcado).IsChecked = true; await Task.Delay(200); await ClicarEm("Adicionar 1 despesa");
            var depoisDeForcar = ArmazenamentoLocal.Ler(arquivoDeDados);
            Verificar(FocoEm("1 despesa adicionada") && TemTexto(Modal()!, t => t.StartsWith("1 foi pulada porque você já tinha")) && depoisDeForcar.Despesas.Count == antesDeCompartilhar.Despesas.Count + 1 && depoisDeForcar.Despesas.Count(e => e.IdDeOrigem is not null) == 1, "A tela de resultado informa uma adicionada e uma pulada");
            await ClicarEm("Fechar"); Verificar(Modal() is null, "A tela de resultado fecha");
            // 13b. Arquivo de outra pessoa: uma despesa nova em uma categoria que não existe aqui, uma que já existe, e "Carla" no lugar de "Bruno".
            var recebido = System.Text.Json.Nodes.JsonNode.Parse(textoDoCompartilhamento.Replace("\"Bruno\"", "\"Carla\""))!; var lista = recebido["Expenses"]!.AsArray();
            lista[0]!["OriginId"] = Guid.NewGuid().ToString(); lista[0]!["Category"] = "Viagens"; recebido["CustomCategories"]!.AsArray().Add("Viagens");
            var outroCaminho = Path.Combine(pastaDoCompartilhamento, "de-outra-pessoa" + ArquivoDeCompartilhamento.Extensao); File.WriteAllText(outroCaminho, recebido.ToJsonString());
            var outra = CompartilhamentoDeDespesas.Ler(outroCaminho); var tituloNovo = outra.Despesas[0].Titulo; var bytesAntesDaOutra = File.ReadAllBytes(arquivoDeDados);
            await AbrirImportacao(outroCaminho);
            Verificar(outra.Pessoas.Contains("Carla") && TemTexto(Modal()!, t => t == "Viagens") && TemTexto(Modal()!, t => t.Contains("Carla")), "O passo 1 resume as pessoas e as categorias que faltam");
            await ClicarEm("Continuar");
            Verificar(Caixa("Pessoa: Carla").SelectedIndex == depoisDeForcar.Pessoas.Count && TemTexto(Modal()!, t => t == "Nova pessoa") && Caixa("Categoria: Viagens").SelectedIndex == 0, "O passo 2 oferece pessoa nova e categoria nova");
            // Duas pessoas do arquivo ligadas à mesma pessoa daqui, dividindo uma despesa: erro claro, sem avançar.
            if (outra.Despesas.Any(e => e.Participantes.Contains("Ana") && e.Participantes.Contains("Carla")))
            {
                Caixa("Pessoa: Carla").SelectedIndex = 0; await Task.Delay(150); await ClicarEm("Continuar");
                Verificar(FocoEm("Quem é quem?") && TemTexto(Modal()!, t => t.Contains("mesma pessoa")), "Ligar duas pessoas do arquivo a uma só é recusado com mensagem clara");
                Caixa("Pessoa: Carla").SelectedIndex = depoisDeForcar.Pessoas.Count; await Task.Delay(150);
            }
            await ClicarEm("Continuar");
            Verificar(TemTexto(Modal()!, t => t.StartsWith("1 será adicionada")) && TemTexto(Modal()!, t => t == "1 já existe · será pulada") && TemTexto(Modal()!, t => t.Contains("você já tem esta despesa")) && SemMudanca(bytesAntesDaOutra), "A prévia mostra uma a adicionar e uma a pular, sem gravar");
            // Com divisão proporcional, a prévia avisa e deixa trocar por divisão igual.
            var proporcional = outra.Despesas[0].Divisao == ModoDeDivisao.RendaDisponivel;
            if (proporcional) { Verificar(Modal()!.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Inlines?.Text?.Contains("recalculado com a renda dos seus dados") == true) && AcharBotaoEm(Modal()!, "Manter proporcional").Classes.Contains("ativo"), "A prévia avisa sobre a divisão proporcional"); await ClicarEm("Dividir igualmente"); }
            await ClicarEm("Adicionar 1 despesa");
            var depoisDaOutra = ArmazenamentoLocal.Ler(arquivoDeDados); var importada2 = depoisDaOutra.Despesas.Single(e => e.IdDeOrigem is not null && e.Titulo == tituloNovo && e.Categoria == "Viagens");
            Verificar(FocoEm("1 despesa adicionada") && TemTexto(Modal()!, t => t == "Carla") && TemTexto(Modal()!, t => t == "Viagens") && TemTexto(Modal()!, t => t.Contains("recuperá-la em Ajustes")), "O resultado lista a pessoa nova e a categoria nova");
            Verificar(depoisDaOutra.Despesas.Count == depoisDeForcar.Despesas.Count + 1 && depoisDaOutra.Pessoas.Select(x => x.Nome).SequenceEqual(depoisDeForcar.Pessoas.Select(x => x.Nome).Append("Carla")) && depoisDaOutra.CategoriasProprias.Contains("Viagens") && (!proporcional || importada2.Divisao == ModoDeDivisao.Igual), "Importar adiciona a despesa, a pessoa e a categoria");
            Verificar(depoisDaOutra.Rendas.SequenceEqual(antesDeCompartilhar.Rendas) && depoisDaOutra.Metas.SequenceEqual(antesDeCompartilhar.Metas) && depoisDaOutra.Acertos.SequenceEqual(antesDeCompartilhar.Acertos)
                && antesDeCompartilhar.Despesas.All(antigo => depoisDaOutra.Despesas.Any(e => e.Id == antigo.Id && e.Valor == antigo.Valor && e.Paga == antigo.Paga && e.Titulo == antigo.Titulo)) && bytesAntesDaOutra.SequenceEqual(File.ReadAllBytes(Path.Combine(diretorio, "financas.backup.json"))), "Importar não toca nos dados existentes e guarda o arquivo anterior como cópia");
            await ClicarEm("Ver despesas");
            Verificar(Modal() is null && TemTexto(principal, t => t == tituloNovo) && principal.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationProperties.GetName(t) == "Buscar despesas"), "Ver despesas abre a página de despesas no mês da despesa nova");
            // 13c. Arquivos recusados: um backup no lugar do compartilhamento e um arquivo corrompido. Nada muda; "Trocar arquivo" recupera.
            var caminhoDoBackup = Path.Combine(pastaDoCompartilhamento, "backup.json"); ArmazenamentoLocal.Exportar(caminhoDoBackup, depoisDaOutra); var caminhoQuebrado = Path.Combine(pastaDoCompartilhamento, "quebrado" + ArquivoDeCompartilhamento.Extensao); File.WriteAllText(caminhoQuebrado, "{"); var bytesDepois = File.ReadAllBytes(arquivoDeDados);
            await AbrirImportacao(caminhoDoBackup);
            Verificar(TemTexto(Modal()!, t => t.Contains("backup") && t.Contains("Nada foi alterado")) && !AcharBotaoEm(Modal()!, "Continuar").IsEnabled, "Um backup é recusado como compartilhamento, em linguagem simples");
            janelaDoApp.CaminhoDeTesteParaAbrir = caminhoQuebrado; await ClicarEm("Trocar arquivo"); await Task.Delay(200);
            Verificar(TemTexto(Modal()!, t => t.Contains("não é um compartilhamento de despesas válido")) && !AcharBotaoEm(Modal()!, "Continuar").IsEnabled, "Um arquivo corrompido é recusado");
            janelaDoApp.CaminhoDeTesteParaAbrir = caminhoDoCompartilhamento; await ClicarEm("Trocar arquivo"); await Task.Delay(200);
            Verificar(AcharBotaoEm(Modal()!, "Continuar").IsEnabled && TemTexto(Modal()!, t => t.StartsWith("Arquivo válido")), "Trocar por um arquivo válido habilita Continuar");
            await ClicarEm("Cancelar"); Verificar(Modal() is null && SemMudanca(bytesDepois), "Arquivos recusados não mudam nada");
            janelaDoApp.CaminhoDeTesteParaAbrir = null; janelaDoApp.CaminhoDeTesteParaSalvar = null;
            try { Directory.Delete(pastaDoCompartilhamento, true); } catch (IOException) { }
            await Clicar("Visão geral", principal);
            // Captura opcional para documentação; os dados exibidos são exclusivamente os dados sintéticos deste teste.
            var saida = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOT");
            if (!string.IsNullOrEmpty(saida))
            {
                // BANKMANAGER_SCREENSHOT_THEME=dark|light permite fotografar o outro tema.
                var temaDaCaptura = Environment.GetEnvironmentVariable("BANKMANAGER_SCREENSHOT_THEME");
                if (temaDaCaptura is "dark" or "light") { Aplicativo.AplicarTema(temaDaCaptura == "dark" ? PreferenciaDeTema.Escuro : PreferenciaDeTema.Claro); await Task.Delay(300); }
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)principal.Bounds.Width, (int)principal.Bounds.Height), new Vector(96, 96));
                bitmap.Render(principal); bitmap.Save(saida, PngBitmapEncoderOptions.Default); Console.WriteLine("Captura: " + saida);
            }
            codigoDeSaida = 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        // Encerra inclusive quando um assert falha, liberando o bloqueio do armazenamento da janela.
        finally { cicloDeVida.Shutdown(codigoDeSaida); }
    }, TimeSpan.FromSeconds(1));
}).StartWithClassicDesktopLifetime(args);
// Limpa a base temporária depois de sair do ciclo de vida gráfico; uma falha de limpeza não altera o teste.
try { Directory.Delete(diretorio, true); } catch (IOException) { }
return codigoDeSaida;
