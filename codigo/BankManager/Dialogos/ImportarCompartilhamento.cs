using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Compartilhar despesas, parte de quem recebe: o assistente "Importar despesas compartilhadas", em três passos (Arquivo, Pessoas,
// Prévia) mais a tela de resultado. As despesas são ADICIONADAS; nada existente é substituído. Toda a lógica vem de CompartilhamentoDeDespesas (Core).
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Pede um arquivo de compartilhamento ao seletor do sistema (ou usa o caminho de teste). Nulo quando a pessoa desiste.
    /// </summary>
    private async Task<string?> EscolherArquivoCompartilhado()
    {
        if (CaminhoDeTesteParaAbrir is not null) return CaminhoDeTesteParaAbrir;
        var arquivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar despesas compartilhadas", AllowMultiple = false, FileTypeFilter = [ArquivosDeCompartilhamento, new FilePickerFileType("Arquivos JSON") { Patterns = ["*.json"] }] });
        return arquivos.Count == 0 ? null : arquivos[0].TryGetLocalPath() ?? throw new ErroDeOrcamento("Escolha um arquivo local.");
    }
    /// <summary>
    /// Indicador de passos "1 Arquivo — 2 Pessoas — 3 Prévia": o atual em círculo preto com o número, os concluídos em verde com ✓.
    /// </summary>
    private static Control IndicadorDePassos(int atual)
    {
        string[] rotulos = ["Arquivo", "Pessoas", "Prévia"]; var fileira = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        for (var i = 0; i < rotulos.Length; i++)
        {
            var estado = i + 1 < atual ? "concluido" : i + 1 == atual ? "atual" : null;
            var circulo = new Border { Child = estado == "concluido" ? Icone("Visto", 14) : new TextBlock { Text = (i + 1).ToString() } }.ComClasse("passo"); var rotulo = Texto(rotulos[i], "rotulo-passo");
            if (estado is not null) { circulo.Classes.Add(estado); rotulo.Classes.Add(estado); }
            if (i > 0) fileira.Children.Add(new Border { Width = 28, VerticalAlignment = VerticalAlignment.Center }.ComClasse("divisoria"));
            fileira.Children.Add(circulo); fileira.Children.Add(rotulo);
        }
        AutomationProperties.SetName(fileira, $"Passo {atual} de 3: {rotulos[atual - 1]}"); return fileira;
    }
    /// <summary>
    /// Abre o assistente de importação. Primeiro o seletor de arquivo; depois, os passos dentro de um diálogo sobreposto. As escolhas
    /// ficam guardadas enquanto o assistente está aberto, então voltar um passo nunca as perde. Só o botão final grava, de uma vez.
    /// </summary>
    private async Task ImportarCompartilhadas()
    {
        if (!pronto || armazenamento is null) throw new ErroDeOrcamento("Os dados ainda não estão disponíveis.");
        if (SobreposicaoAberta) return;
        var escolhido = await EscolherArquivoCompartilhado(); if (escolhido is null) return;
        // ----- Estado do assistente (vive nesta chamada; cada passo é redesenhado a partir dele) -----
        var caminho = escolhido; ArquivoDeCompartilhamento? arquivo = null; string? erroDoArquivo = null; long tamanho = 0;
        var nomesLocais = dados.Pessoas.Select(p => p.Nome).ToList();
        // Escolha por pessoa do arquivo: índice de uma pessoa existente, depois "criar" e "ignorar". Categorias: 0 cria, 1 usa "Outros".
        var escolhaDePessoa = new Dictionary<string, int>(); var escolhaDeCategoria = new Dictionary<string, int>(); var responsavelPadrao = 0;
        var igual = false; var mesmoAssim = new HashSet<Guid>(); var mexeu = false;
        int IndiceDeCriar() => nomesLocais.Count; int IndiceDeIgnorar() => nomesLocais.Count + 1;
        // Lê e valida o arquivo. Qualquer problema vira uma explicação em linguagem simples; nada é alterado.
        void Carregar(string escolhidas)
        {
            caminho = escolhidas; arquivo = null; erroDoArquivo = null; escolhaDePessoa.Clear(); escolhaDeCategoria.Clear(); responsavelPadrao = 0; igual = false; mesmoAssim.Clear();
            try
            {
                RecusarCaminhoInterno(escolhidas); tamanho = new FileInfo(escolhidas).Length; var lido = CompartilhamentoDeDespesas.Ler(escolhidas); var sugestao = CompartilhamentoDeDespesas.Sugerir(dados, lido);
                foreach (var nome in lido.Pessoas) escolhaDePessoa[nome] = sugestao.Pessoas[nome].Acao == AcaoDaPessoa.Existente ? dados.Pessoas.FindIndex(p => p.Id == sugestao.Pessoas[nome].IdDaPessoa) : IndiceDeCriar();
                foreach (var nome in CompartilhamentoDeDespesas.CategoriasQueFaltam(dados, lido)) escolhaDeCategoria[nome] = sugestao.CriarCategorias.GetValueOrDefault(nome) ? 0 : 1;
                arquivo = lido;
            }
            catch (Exception ex)
            {
                var motivo = ex is ErroDeOrcamento ? ex.Message : "Não foi possível ler o arquivo. Confira se ele ainda existe e se você tem permissão para abri-lo.";
                erroDoArquivo = motivo.Contains("Nada foi alterado") || motivo.Contains("foram preservados") ? motivo : motivo + " Nada foi alterado nos seus dados.";
            }
        }
        // Traduz as escolhas da tela para o mapeamento que o Core entende.
        MapeamentoDoCompartilhamento Mapeamento() => new()
        {
            Pessoas = escolhaDePessoa.ToDictionary(p => p.Key, p => p.Value < nomesLocais.Count ? new EscolhaDaPessoa(AcaoDaPessoa.Existente, dados.Pessoas[p.Value].Id) : new EscolhaDaPessoa(p.Value == IndiceDeCriar() ? AcaoDaPessoa.Criar : AcaoDaPessoa.Ignorar)),
            CriarCategorias = escolhaDeCategoria.ToDictionary(c => c.Key, c => c.Value == 0), ResponsavelPadrao = responsavelPadrao > 0 ? dados.Pessoas[responsavelPadrao - 1].Id : null,
            IgualEmVezDeProporcional = igual, ImportarMesmoAssim = mesmoAssim.ToHashSet()
        };
        string Plural(int n, string uma, string muitas) => n == 1 ? uma : muitas;
        // Esc pede confirmação quando já há escolhas feitas; "Cancelar" e o "X" são pedidos explícitos e fecham direto.
        async void TeclaEsc()
        {
            if (!mexeu || await Confirmar(this, "Sair da importação?", "As escolhas feitas neste assistente serão descartadas. Nada foi salvo e seus dados continuam como estavam.")) FecharSobreposicao();
        }
        void Passo1()
        {
            var mudanca = Botao("Trocar arquivo", () => _ = Executar(async () => { var outra = await EscolherArquivoCompartilhado(); if (outra is null) return; Carregar(outra); mexeu = false; Passo1(); })); Grid.SetColumn(mudanca, 2); mudanca.VerticalAlignment = VerticalAlignment.Center;
            var informacao = Pilha(Texto(Path.GetFileName(caminho), "mono").ComClasse("forte"), Texto($"Escolhido agora · {(tamanho < 1024 ? $"{tamanho} bytes" : tamanho < 1024 * 1024 ? $"{Math.Max(1, tamanho / 1024)} KB" : $"{tamanho / 1024d / 1024d:0.#} MB")}", "apagado").ComClasse("pequeno")); informacao.Spacing = 2; informacao.VerticalAlignment = VerticalAlignment.Center; informacao.Margin = new Thickness(14, 0); Grid.SetColumn(informacao, 1);
            ((TextBlock)informacao.Children[0]).TextTrimming = TextTrimming.CharacterEllipsis;
            var cartaoDoArquivo = new Border { Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { new Border { Child = Icone("Arquivo", 20) }.ComClasse("bloco"), informacao, mudanca } } }.ComClasse("painel");
            var corpo = Pilha(cartaoDoArquivo); corpo.Spacing = 14;
            var proximo = Botao("Continuar", () => { mexeu = true; Passo2(); }, "primario"); proximo.IsEnabled = arquivo is not null;
            if (arquivo is null) corpo.Children.Add(Faixa("Alerta", erroDoArquivo ?? "Arquivo inválido.", "negativo"));
            else
            {
                corpo.Children.Add(Faixa("Visto", "Arquivo válido, gerado pelo BankManager" + (arquivo.VersaoDoAplicativo.Length > 0 ? " " + arquivo.VersaoDoAplicativo : ""), "positivo"));
                var faltando = escolhaDeCategoria.Keys.ToList();
                var esquerda = Pilha(LinhaDeResumo("Despesas", arquivo.Despesas.Count.ToString()), LinhaDeResumo("Pessoas no arquivo", string.Join(", ", arquivo.Pessoas))); esquerda.Spacing = 0; esquerda.Margin = new Thickness(0, 0, 14, 0);
                var direita = Pilha(LinhaDeResumo("Total", Dinheiro.Formatar(arquivo.Despesas.Sum(e => e.Valor))), LinhaDeResumo(Plural(faltando.Count, "Categoria que você pode não ter", "Categorias que você pode não ter"), faltando.Count == 0 ? "Nenhuma" : string.Join(", ", faltando))); direita.Spacing = 0; direita.Margin = new Thickness(14, 0, 0, 0); Grid.SetColumn(direita, 1);
                corpo.Children.Add(new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Children = { esquerda, direita } });
                corpo.Children.Add(Faixa("Informacao", "Antes de salvar, você confere tudo na prévia. Um backup da versão anterior dos seus dados é guardado automaticamente."));
            }
            var (cartao, tituloDaSecao) = CartaoModal("Importar despesas compartilhadas", "As despesas serão adicionadas aos seus dados, sem apagar nem trocar nada.", corpo, RodapeModal(null, Botao("Cancelar", FecharSobreposicao), proximo), FecharSobreposicao, IndicadorDePassos(1));
            MostrarSobreposicao(cartao, TeclaEsc, tituloDaSecao);
        }
        void Passo2()
        {
            var compartilhada = arquivo!; var erro = Texto("", "erro"); erro.IsVisible = false;
            var caixaDoResponsavelPadrao = ListaDeOpcoes(new[] { "Escolha uma pessoa…" }.Concat(nomesLocais), responsavelPadrao);
            var campoDoResponsavelPadrao = Campo("Despesas que ficarem sem responsável vão para", caixaDoResponsavelPadrao, "Usado quando todas as pessoas de uma despesa foram ignoradas."); AutomationProperties.SetName(caixaDoResponsavelPadrao, "Quem assume as despesas sem responsável");
            caixaDoResponsavelPadrao.SelectionChanged += (_, _) => { if (caixaDoResponsavelPadrao.SelectedIndex >= 0) { responsavelPadrao = caixaDoResponsavelPadrao.SelectedIndex; mexeu = true; erro.IsVisible = false; } };
            void MostrarResponsavelPadrao() => campoDoResponsavelPadrao.IsVisible = escolhaDePessoa.Values.Any(v => v == IndiceDeIgnorar());
            // Linha "no arquivo → aqui": ícone ou avatar, nome, seta, lista de escolhas e, se houver, um chip com a situação.
            Control LinhaDeMapeamento(Control abertura, string nome, string legenda, ComboBox caixa, Control? etiqueta)
            {
                var quem = Pilha(new TextBlock { Text = nome, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, Texto(legenda, "apagado").ComClasse("pequeno")); quem.Spacing = 1; quem.VerticalAlignment = VerticalAlignment.Center; quem.Margin = new Thickness(12, 0, 8, 0);
                var seta = Icone("SetaParaDireita", 16); seta.Margin = new Thickness(0, 0, 14, 0); caixa.VerticalAlignment = VerticalAlignment.Center;
                var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,170,Auto,*,Auto"), Margin = new Thickness(0, 10) };
                Control[] celulas = etiqueta is null ? [abertura, quem, seta, caixa] : [abertura, quem, seta, caixa, etiqueta];
                for (var i = 0; i < celulas.Length; i++) { Grid.SetColumn(celulas[i], i); grade.Children.Add(celulas[i]); }
                return grade;
            }
            var corpo = Pilha(Texto("Pessoas", "secao"), Divisoria()); corpo.Spacing = 0;
            foreach (var nome in compartilhada.Pessoas)
            {
                var caixa = ListaDeOpcoes(nomesLocais.Concat([$"Criar nova pessoa “{nome}”", "Ignorar esta pessoa"]), escolhaDePessoa[nome]); AutomationProperties.SetName(caixa, "Pessoa: " + nome);
                var lugarDaEtiqueta = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), MinWidth = 96 };
                // Chip: "Mesmo nome" quando a pessoa escolhida tem o mesmo nome (sem diferenciar maiúsculas e acentos), "Nova pessoa" ou "Ignorada".
                void MostrarEtiqueta()
                {
                    var escolha = escolhaDePessoa[nome];
                    lugarDaEtiqueta.Content = escolha == IndiceDeCriar() ? Etiqueta("Nova pessoa", "situacao") : escolha == IndiceDeIgnorar() ? Etiqueta("Ignorada", "alerta") : BuscaDeDespesas.Simplificar(nomesLocais[escolha]) == BuscaDeDespesas.Simplificar(nome) ? Etiqueta("Mesmo nome", "positivo") : Etiqueta("Outra pessoa", "situacao");
                }
                caixa.SelectionChanged += (_, _) => { if (caixa.SelectedIndex < 0) return; escolhaDePessoa[nome] = caixa.SelectedIndex; mexeu = true; erro.IsVisible = false; MostrarEtiqueta(); MostrarResponsavelPadrao(); };
                MostrarEtiqueta(); corpo.Children.Add(LinhaDeMapeamento(Avatar(nome).ComClasse("grande"), nome, "no arquivo", caixa, lugarDaEtiqueta)); corpo.Children.Add(Divisoria());
            }
            var ajuda = Texto("Se você ignorar alguém, as despesas dessa pessoa ficam com os outros responsáveis; as que ficarem sem ninguém vão para quem você escolher aqui. Nenhuma despesa fica sem responsável.", "apagado"); ajuda.Classes.Add("pequeno"); ajuda.Margin = new Thickness(0, 12, 0, 0);
            corpo.Children.Add(ajuda); campoDoResponsavelPadrao.Margin = new Thickness(0, 12, 0, 0); corpo.Children.Add(campoDoResponsavelPadrao); MostrarResponsavelPadrao();
            if (escolhaDeCategoria.Count > 0)
            {
                var titulo = Texto("Categorias", "secao"); titulo.Margin = new Thickness(0, 22, 0, 12); corpo.Children.Add(titulo); corpo.Children.Add(Divisoria());
                foreach (var nome in escolhaDeCategoria.Keys.ToList())
                {
                    var caixa = ListaDeOpcoes([$"Criar a categoria “{nome}”", "Usar “Outros”"], escolhaDeCategoria[nome]); AutomationProperties.SetName(caixa, "Categoria: " + nome);
                    caixa.SelectionChanged += (_, _) => { if (caixa.SelectedIndex < 0) return; escolhaDeCategoria[nome] = caixa.SelectedIndex; mexeu = true; erro.IsVisible = false; };
                    corpo.Children.Add(LinhaDeMapeamento(new Border { Child = Icone("Etiqueta", 18), Background = null }.ComClasse("bloco").ComClasse("painel"), nome, "você ainda não tem", caixa, null)); corpo.Children.Add(Divisoria());
                }
            }
            erro.Margin = new Thickness(0, 12, 0, 0); corpo.Children.Add(erro);
            // Continuar só avança se o mapeamento funciona de verdade: o cálculo é o mesmo da prévia, em uma cópia dos dados.
            var proximo = Botao("Continuar", () =>
            {
                try { CompartilhamentoDeDespesas.Importar(dados, compartilhada, Mapeamento()); Passo3(); }
                catch (Exception ex) { erro.Text = ErroAmigavel(ex); erro.IsVisible = true; erro.BringIntoView(); }
            }, "primario");
            var (cartao, tituloDaSecao) = CartaoModal("Quem é quem?", "Os nomes do arquivo podem ser diferentes dos seus. Confirme cada um.", corpo, RodapeModal(Botao("Voltar", Passo1), Botao("Cancelar", FecharSobreposicao), proximo), FecharSobreposicao, IndicadorDePassos(2));
            MostrarSobreposicao(cartao, TeclaEsc, tituloDaSecao);
        }
        void Passo3()
        {
            var compartilhada = arquivo!; var resumo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 }; var erro = new ContentControl();
            var adicionar = Botao("Adicionar", () => { }, "primario"); ResultadoDaImportacao? previa = null;
            // Uma linha por despesa do arquivo. Os controles são criados uma vez e só atualizados, para o foco não se perder ao marcar uma caixa.
            var fileiras = compartilhada.Despesas.Select(fonte =>
            {
                var titulo = new TextBlock { Text = fonte.Titulo, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis }; var detalhe = Texto("", "apagado"); detalhe.Classes.Add("pequeno");
                var forcar = new CheckBox { Content = "Importar mesmo assim, como outra despesa", IsChecked = mesmoAssim.Contains(fonte.IdDeOrigem), IsVisible = false, FontSize = 13 }; AutomationProperties.SetName(forcar, "Importar mesmo assim: " + fonte.Titulo);
                var texto = Pilha(titulo, detalhe, forcar); texto.Spacing = 1; texto.VerticalAlignment = VerticalAlignment.Center;
                var etiqueta = new ContentControl { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) }; Grid.SetColumn(etiqueta, 1);
                var valor = Texto(Dinheiro.Formatar(fonte.Valor), "forte"); valor.VerticalAlignment = VerticalAlignment.Center; valor.TextAlignment = TextAlignment.Right; valor.MinWidth = 100; Grid.SetColumn(valor, 2);
                var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 9), Children = { texto, etiqueta, valor } };
                return (Origem: fonte, Titulo: titulo, Detail: detalhe, Force: forcar, Etiqueta: etiqueta, Valor: valor, Visao: grade);
            }).ToList();
            // Recalcula a prévia em uma cópia dos dados (nada é gravado) e atualiza chips, linhas e o botão final.
            void Atualizar()
            {
                try { previa = CompartilhamentoDeDespesas.Importar(dados, compartilhada, Mapeamento()).Result; erro.Content = null; }
                catch (Exception ex) { previa = null; erro.Content = Faixa("Alerta", ErroAmigavel(ex), "negativo"); }
                resumo.Children.Clear(); var n = previa?.Adicionadas.Count ?? 0;
                if (previa is not null)
                {
                    resumo.Children.Add(Etiqueta($"{n} {Plural(n, "será adicionada", "serão adicionadas")} · {Dinheiro.Formatar(previa.Adicionadas.Sum(e => e.Valor))}", "positivo"));
                    if (previa.Duplicadas > 0) resumo.Children.Add(Etiqueta($"{previa.Duplicadas} já {Plural(previa.Duplicadas, "existe · será pulada", "existem · serão puladas")}", "alerta"));
                    if (previa.Erros.Count > 0) resumo.Children.Add(Etiqueta($"{previa.Erros.Count} com erro · {Plural(previa.Erros.Count, "não será importada", "não serão importadas")}", "negativo"));
                    foreach (var etiqueta in resumo.Children.OfType<Border>()) { etiqueta.Padding = new Thickness(14, 7); ((TextBlock)etiqueta.Child!).FontSize = 13; ((TextBlock)etiqueta.Child!).FontWeight = FontWeight.SemiBold; }
                }
                for (var i = 0; i < fileiras.Count; i++)
                {
                    var fileira = fileiras[i]; var linha = previa?.Linhas[i]; var quando = $"{fileira.Origem.Vencimento:dd/MM}" + (fileira.Origem.Vencimento.Year != DateTime.Today.Year ? $"/{fileira.Origem.Vencimento:yy}" : "");
                    var categoria = fileira.Origem.Categoria + (fileira.Origem.TotalDeParcelas > 1 ? $" · parcela {fileira.Origem.NumeroDaParcela} de {fileira.Origem.TotalDeParcelas}" : "");
                    fileira.Force.IsVisible = linha?.JaExiste == true; var puladas = linha?.Situacao != SituacaoDaLinha.Adicionada;
                    fileira.Titulo.Classes.Set("apagado", puladas); fileira.Valor.Classes.Set("apagado", puladas);
                    fileira.Etiqueta.Content = linha?.Situacao switch { SituacaoDaLinha.Adicionada => Etiqueta("Adicionar", "positivo"), SituacaoDaLinha.Pulada => Etiqueta("Pular", "alerta"), SituacaoDaLinha.Erro => Etiqueta("Com erro", "negativo"), _ => null };
                    fileira.Detail.Text = linha?.Situacao switch
                    {
                        // Pessoas como ficarão aqui (depois do mapeamento), ligadas por "e".
                        SituacaoDaLinha.Adicionada => $"{linha.Despesa!.Categoria}{categoria[fileira.Origem.Categoria.Length..]} · {quando} · {string.Join(" e ", fileira.Origem.Participantes.Where(p => escolhaDePessoa[p] != IndiceDeIgnorar()).Select(p => escolhaDePessoa[p] < nomesLocais.Count ? nomesLocais[escolhaDePessoa[p]] : p).DefaultIfEmpty(responsavelPadrao > 0 ? nomesLocais[responsavelPadrao - 1] : "—"))}",
                        SituacaoDaLinha.Pulada => $"{categoria} · {quando} · você já tem esta despesa",
                        SituacaoDaLinha.Erro => $"{categoria} · {quando} · {linha.Erro}",
                        _ => $"{categoria} · {quando}"
                    };
                }
                var rotulo = n == 0 ? "Nada a adicionar" : $"Adicionar {n} {Plural(n, "despesa", "despesas")}"; adicionar.Content = rotulo; AutomationProperties.SetName(adicionar, rotulo); adicionar.IsEnabled = n > 0;
            }
            var lista = new StackPanel();
            foreach (var fileira in fileiras)
            {
                lista.Children.Add(Divisoria()); lista.Children.Add(fileira.Visao);
                fileira.Force.IsCheckedChanged += (_, _) => { if (fileira.Force.IsChecked == true) mesmoAssim.Add(fileira.Origem.IdDeOrigem); else mesmoAssim.Remove(fileira.Origem.IdDeOrigem); mexeu = true; Atualizar(); };
            }
            lista.Children.Add(Divisoria());
            var corpo = Pilha(resumo, lista, erro); corpo.Spacing = 12;
            if (compartilhada.Despesas.Any(e => e.Divisao == ModoDeDivisao.RendaDisponivel))
            {
                // Divisão proporcional: o rateio depende da renda de cada pessoa NESTA base; quem importa decide se mantém o modo.
                var aviso = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
                aviso.Inlines!.Add(new Avalonia.Controls.Documents.Run("Divisão proporcional à renda. ") { FontWeight = FontWeight.SemiBold }); aviso.Inlines.Add(new Avalonia.Controls.Documents.Run("O valor de cada um será recalculado com a renda dos seus dados, então pode ficar diferente do que a outra pessoa viu."));
                var informacao = Icone("Informacao", 18); informacao.VerticalAlignment = VerticalAlignment.Top; informacao.Margin = new Thickness(0, 1, 12, 0); Grid.SetColumn(aviso, 1);
                var modo = Segmentado(["Manter proporcional", "Dividir igualmente"], igual ? 1 : 0, i => { igual = i == 1; mexeu = true; Atualizar(); }).ComClasse("escuro"); modo.Margin = new Thickness(30, 0, 0, 0);
                var bloco = Pilha(new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { informacao, aviso } }, modo); bloco.Spacing = 12;
                corpo.Children.Add(new Border { Child = bloco }.ComClasse("painel"));
            }
            adicionar.Click += (_, _) =>
            {
                try
                {
                    // Uma única gravação: a alteração é feita em uma cópia, o orçamento inteiro é validado e só então o arquivo é trocado
                    // (a versão anterior vira a cópia automática). Qualquer falha deixa tudo como estava.
                    ResultadoDaImportacao? resultado = null; var mapeamento = Mapeamento();
                    Gravar(d => resultado = CompartilhamentoDeDespesas.Aplicar(d, compartilhada, mapeamento));
                    MostrarResultado(resultado!);
                }
                catch (Exception ex) { erro.Content = Faixa("Alerta", (ex is ErroDeOrcamento ? ex.Message : "Não foi possível salvar. Verifique o espaço em disco e as permissões da pasta de dados.") + " Nada foi alterado nos seus dados.", "negativo"); }
            };
            Atualizar();
            var (cartao, tituloDaSecao) = CartaoModal("Confira antes de adicionar", "Nada foi salvo ainda. Seus dados atuais não serão alterados.", corpo, RodapeModal(Botao("Voltar", Passo2), Botao("Cancelar", FecharSobreposicao), adicionar), FecharSobreposicao, IndicadorDePassos(3));
            MostrarSobreposicao(cartao, TeclaEsc, tituloDaSecao);
        }
        // Tela final: o que entrou, o que foi pulado e o lembrete da cópia anterior. "Ver despesas" abre o mês da primeira despesa adicionada.
        void MostrarResultado(ResultadoDaImportacao resultado)
        {
            var n = resultado.Adicionadas.Count; var puladas = resultado.Duplicadas + resultado.Erros.Count;
            var titulo = Texto($"{n} {Plural(n, "despesa adicionada", "despesas adicionadas")}", "modal-titulo"); titulo.FontSize = 26; titulo.TextAlignment = TextAlignment.Center; titulo.Focusable = true; AutomationProperties.SetHeadingLevel(titulo, 1);
            var partes = new List<string>();
            if (resultado.Duplicadas > 0) partes.Add($"{resultado.Duplicadas} {Plural(resultado.Duplicadas, "foi pulada porque você já tinha", "foram puladas porque você já tinha")}.");
            if (resultado.Erros.Count > 0) partes.Add($"{resultado.Erros.Count} {Plural(resultado.Erros.Count, "tinha erro e não foi importada", "tinham erro e não foram importadas")}.");
            partes.Add("Todo o resto dos seus dados continua como estava.");
            var frase = Texto(string.Join(" ", partes), "modal-subtitulo"); frase.TextAlignment = TextAlignment.Center; frase.FontSize = 14;
            string Rotulo(Despesa e) => e.Titulo + (e.TotalDeParcelas > 1 ? $" (parcela {e.NumeroDaParcela}/{e.TotalDeParcelas})" : "");
            var linhas = new List<(string Rotulo, string Valor)> { ("Adicionadas", string.Join(", ", resultado.Adicionadas.Take(3).Select(Rotulo)) + (n > 3 ? $" e mais {n - 3}" : "")) };
            if (resultado.PessoasNovas.Count > 0) linhas.Add((Plural(resultado.PessoasNovas.Count, "Pessoa nova criada", "Pessoas novas criadas"), string.Join(", ", resultado.PessoasNovas)));
            if (resultado.CategoriasNovas.Count > 0) linhas.Add((Plural(resultado.CategoriasNovas.Count, "Categoria nova criada", "Categorias novas criadas"), string.Join(", ", resultado.CategoriasNovas)));
            linhas.Add(("Total adicionado", Dinheiro.Formatar(resultado.Adicionadas.Sum(e => e.Valor))));
            var resumo = new StackPanel(); for (var i = 0; i < linhas.Count; i++) resumo.Children.Add(LinhaDeResumo(linhas[i].Rotulo, linhas[i].Valor, i < linhas.Count - 1));
            var ver = Botao("Ver despesas", () =>
            {
                // Vai para a página Despesas, sem filtros, no mês da primeira despesa adicionada.
                mes = Datas.Mes(resultado.Adicionadas.Min(e => e.Vencimento)); todosOsMeses = false; filtro = ""; filtroDeSituacao = 0; filtroDeCategoria = 0; FecharSobreposicao(); IrPara(2);
            }, "primario");
            var botoes = Linha(Botao("Fechar", FecharSobreposicao), ver); botoes.HorizontalAlignment = HorizontalAlignment.Center; botoes.Margin = new Thickness(0, 6, 0, 0);
            var conteudo = Pilha(new Border { Child = Icone("Visto", 30) }.ComClasse("circulo-sucesso"), titulo, frase, new Border { Child = resumo, Margin = new Thickness(0, 8, 0, 0) }.ComClasse("painel"),
                Faixa("Escudo", "Uma cópia da versão anterior dos seus dados foi guardada. Dá para recuperá-la em Ajustes."), botoes); conteudo.Spacing = 12;
            var cartao = new Border { Child = new ScrollViewer { Content = conteudo }, Width = 560 }.ComClasse("modal");
            AutomationProperties.SetName(cartao, titulo.Text!); AutomationProperties.SetControlTypeOverride(cartao, Avalonia.Automation.Peers.AutomationControlType.Window);
            Avalonia.Input.KeyboardNavigation.SetTabNavigation(cartao, Avalonia.Input.KeyboardNavigationMode.Cycle);
            MostrarSobreposicao(cartao, FecharSobreposicao, titulo);
        }
        Carregar(caminho); EsconderAviso(); Passo1();
    }
}
