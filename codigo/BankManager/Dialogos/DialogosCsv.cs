using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Planilhas: exportar as despesas em CSV e importar um extrato bancário em CSV, com um assistente de prévia.
public sealed partial class JanelaPrincipal
{
    private static readonly FilePickerFileType ArquivosCsv = new("Planilha CSV") { Patterns = ["*.csv", "*.txt"] };
    /// <summary>
    /// Pede onde salvar e grava todas as despesas em CSV (Excel em português: ponto e vírgula, vírgula decimal, UTF-8 com BOM).
    /// </summary>
    private async Task ExportarCsv()
    {
        var arquivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Exportar despesas", SuggestedFileName = $"despesas-{DateTime.Today:yyyy-MM-dd}.csv", DefaultExtension = "csv", FileTypeChoices = [new FilePickerFileType("Planilha CSV") { Patterns = ["*.csv"] }] });
        if (arquivo is null) return;
        var caminho = arquivo.TryGetLocalPath() ?? throw new ErroDeOrcamento("Escolha uma pasta local.");
        RecusarCaminhoInterno(caminho); ExportacaoCsv.Salvar(caminho, ExportacaoCsv.Despesas(dados));
        Avisar($"{dados.Despesas.Count} despesas exportadas.", sucesso: true);
    }
    /// <summary>
    /// Pede um arquivo e abre o assistente de importação de extrato.
    /// </summary>
    private async Task ImportarExtrato()
    {
        if (!pronto) throw new ErroDeOrcamento("Os dados ainda não estão disponíveis.");
        var arquivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar extrato", AllowMultiple = false, FileTypeFilter = [ArquivosCsv] });
        if (arquivos.Count == 0) return;
        await ImportarExtratoDoArquivo(arquivos[0].TryGetLocalPath() ?? throw new ErroDeOrcamento("Escolha um arquivo local."));
    }
    /// <summary>
    /// Lê o CSV e abre o assistente: escolher as colunas (já sugeridas), o sinal das despesas e o responsável, vendo ao vivo o que será
    /// importado. Só grava ao confirmar. Público porque os testes de interface o chamam sem passar pelo seletor de arquivos.
    /// </summary>
    public async Task ImportarExtratoDoArquivo(string caminho)
    {
        var tabela = Csv.Interpretar(ImportacaoDeExtrato.LerArquivo(caminho)); var palpite = ImportacaoDeExtrato.Adivinhar(tabela); var largura = tabela.Linhas.Max(r => r.Count);
        var cabecalho = new CheckBox { Content = "A primeira linha é o cabeçalho (nomes das colunas)", IsChecked = palpite.TemCabecalho };
        // Nome de cada coluna: o do cabeçalho; sem cabeçalho, um exemplo do conteúdo para a pessoa reconhecer a coluna.
        List<string> NomesDasColunas() => Enumerable.Range(0, largura).Select(c => cabecalho.IsChecked == true ? $"Coluna {c + 1}: {(c < tabela.Linhas[0].Count ? tabela.Linhas[0][c] : "")}" : $"Coluna {c + 1} (ex.: {(c < tabela.Linhas[0].Count ? tabela.Linhas[0][c] : "")})").ToList();
        var data = ListaDeOpcoes(NomesDasColunas(), palpite.ColunaDaData); var descricao = ListaDeOpcoes(NomesDasColunas(), palpite.ColunaDaDescricao); var valor = ListaDeOpcoes(NomesDasColunas(), palpite.ColunaDoValor);
        var sinal = ListaDeOpcoes(["Negativos (extrato de conta corrente)", "Positivos (fatura de cartão)"], 0);
        // Responsável: uma pessoa (despesa individual) ou, havendo mais de uma pessoa, todas com divisão igual ou pela renda disponível.
        var quem = dados.Pessoas.Select(p => p.Nome).ToList(); if (dados.Pessoas.Count > 1) { quem.Add("Todos, em partes iguais"); quem.Add("Todos, pela renda disponível"); }
        var responsavel = ListaDeOpcoes(quem, 0);
        var previa = Texto("", "muted"); var erro = Texto("", "error"); Window? dialogo = null; PlanoDeImportacao? plano = null; var montando = true;
        var importar = Botao("Importar", () => { }, "primary"); importar.IsEnabled = false;
        // Recalcula o plano com as escolhas atuais e mostra o resultado; sem despesas a importar, o botão fica desligado.
        void Recalcular()
        {
            if (montando || data.SelectedIndex < 0 || descricao.SelectedIndex < 0 || valor.SelectedIndex < 0 || responsavel.SelectedIndex < 0) return;
            try
            {
                var indice = responsavel.SelectedIndex; var todos = dados.Pessoas.Select(p => p.Id).ToList();
                var (divisao, pessoas) = indice < dados.Pessoas.Count ? (ModoDeDivisao.Individual, [dados.Pessoas[indice].Id]) : (indice == dados.Pessoas.Count ? ModoDeDivisao.Igual : ModoDeDivisao.RendaDisponivel, todos);
                plano = ImportacaoDeExtrato.Planejar(dados, tabela, new MapeamentoDeImportacao(data.SelectedIndex, descricao.SelectedIndex, valor.SelectedIndex, cabecalho.IsChecked == true, sinal.SelectedIndex == 0 ? SinalDoValor.NegativoEDespesa : SinalDoValor.PositivoEDespesa), divisao, pessoas);
                var linhas = new List<string> { $"{plano.Despesas.Count} {(plano.Despesas.Count == 1 ? "despesa será importada" : "despesas serão importadas")} (já pagas, na categoria Outros)." };
                if (plano.EntradasIgnoradas > 0) linhas.Add($"{plano.EntradasIgnoradas} linha(s) ignorada(s): entradas, estornos ou valor zero.");
                if (plano.Duplicadas > 0) linhas.Add($"{plano.Duplicadas} linha(s) repetida(s): já existem com a mesma data, valor e descrição.");
                if (plano.TotalDeErros > 0) linhas.Add($"{plano.TotalDeErros} linha(s) com erro, que não serão importadas: " + string.Join("; ", plano.Erros.Take(3)) + (plano.TotalDeErros > 3 ? "…" : ""));
                linhas.AddRange(plano.Despesas.Take(5).Select(e => $"• {e.Vencimento:dd/MM/yyyy}  {e.Titulo}  {Dinheiro.Formatar(e.Valor)}"));
                if (plano.Despesas.Count > 5) linhas.Add($"… e mais {plano.Despesas.Count - 5}.");
                previa.Text = string.Join("\n", linhas); erro.Text = ""; importar.IsEnabled = plano.Despesas.Count > 0;
            }
            catch (Exception ex) { plano = null; previa.Text = ""; erro.Text = ErroAmigavel(ex); importar.IsEnabled = false; }
        }
        cabecalho.IsCheckedChanged += (_, _) =>
        {
            // Os nomes das colunas mudam com o cabeçalho; troca as listas sem perder as escolhas.
            montando = true; foreach (var caixa in new[] { data, descricao, valor }) { var manter = caixa.SelectedIndex; caixa.ItemsSource = NomesDasColunas(); caixa.SelectedIndex = manter; }
            montando = false; Recalcular();
        };
        foreach (var caixa in new[] { data, descricao, valor, sinal, responsavel }) caixa.SelectionChanged += (_, _) => Recalcular();
        AutomationProperties.SetName(data, "Coluna da data"); AutomationProperties.SetName(descricao, "Coluna da descrição"); AutomationProperties.SetName(valor, "Coluna do valor"); AutomationProperties.SetName(responsavel, "Responsável");
        var painel = Pilha(Cabecalho("Importar extrato", $"{Path.GetFileName(caminho)} · {tabela.Linhas.Count} linhas. Confira as colunas: cada saída vira uma despesa já paga."), cabecalho,
            Colunas(Campo("Data", data), Campo("Descrição", descricao)), Colunas(Campo("Valor", valor), Campo("Valores que são despesas", sinal)),
            Campo("Responsável pelas despesas", responsavel, "Você pode editar cada despesa depois, por exemplo para mudar a categoria."), Texto("O que será importado", "section"), previa, erro);
        dialogo = Dialogo("Importar extrato", painel, 760); dialogo.Height = 820;
        importar.Click += (_, _) =>
        {
            if (plano is null || plano.Despesas.Count == 0) return;
            var criadas = plano.Despesas; var quantidade = criadas.Count;
            try { Gravar(d => d.Despesas.AddRange(criadas)); Avisar($"{quantidade} {(quantidade == 1 ? "despesa importada" : "despesas importadas")}. Edite cada uma para escolher a categoria.", sucesso: true); dialogo.Close(); }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); }
        };
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close()), importar));
        montando = false; Recalcular();
        await dialogo.ShowDialog(this);
    }
}
