using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Página "Pessoas e rendas": cadastro de quem divide as despesas, a renda de cada mês e quanto cada pessoa assume das despesas.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Lista pessoas em linhas: renda da competência, parte das despesas que cada uma assume e quanto sobra, com edição e exclusão
    /// protegida por referências no histórico.
    /// </summary>
    private Control PaginaDePessoas()
    {
        var acoes = Linha(Botao("Adicionar pessoa", () => _ = Executar(() => EditarPessoa()), "escuro", "Mais"), Botao("Copiar rendas do mês anterior", () => _ = Executar(CopiarRendas)));
        var painel = Pilha(CabecalhoDaPagina("Pessoas e rendas", "Quem divide as despesas, quanto cada pessoa recebe e quanto assume neste mês."), acoes); painel.Spacing = 18;
        if (dados.Pessoas.Count == 0) { painel.Children.Add(CartaoVazio("Ninguém por aqui ainda", "Adicione você e as pessoas que compartilham as despesas. Depois informe a renda de cada uma neste mês.")); return painel; }
        var saldos = MotorFinanceiro.Saldos(dados, mes).ToDictionary(b => b.Pessoa.Id);
        var fileiras = new StackPanel();
        foreach (var pessoa in dados.Pessoas)
        {
            if (fileiras.Children.Count > 0) fileiras.Children.Add(Divisoria());
            // Resumo das rendas do mês: nenhuma (alerta), uma (só o nome) ou várias ("Salário R$ 3.000,00 · Extra R$ 500,00").
            var linhas = MotorFinanceiro.LinhasDeRenda(dados, pessoa.Id, mes);
            var situacao = linhas.Count == 0 ? "Renda ainda não informada neste mês" : linhas.Count == 1 ? linhas[0].Rotulo : string.Join(" · ", linhas.Select(l => $"{l.Rotulo} {Dinheiro.Formatar(l.Valor)}"));
            var quem = Pilha(new TextBlock { Text = pessoa.Nome, FontWeight = FontWeight.SemiBold }, Texto(situacao, linhas.Count == 0 ? "alerta" : "apagado").ComClasse("pequeno")); quem.Spacing = 1; quem.VerticalAlignment = VerticalAlignment.Center; quem.Margin = new Thickness(12, 0);
            // Divisão do mês: quanto a pessoa assume das despesas e quanto isso compromete da renda dela.
            var b = saldos[pessoa.Id]; var comprometido = b.Renda > 0 ? (double)Math.Min(100, b.Atribuido / b.Renda * 100) : 0;
            var compartilhar = Pilha(Texto(b.Renda > 0 ? $"Assume {Dinheiro.Formatar(b.Atribuido)} · {comprometido:0}% da renda" : $"Assume {Dinheiro.Formatar(b.Atribuido)}", "apagado").ComClasse("pequeno"), new ProgressBar { Value = comprometido, Maximum = 100, Height = 6 }.ComClasse("neutro")); compartilhar.Spacing = 6; compartilhar.VerticalAlignment = VerticalAlignment.Center; compartilhar.Margin = new Thickness(0, 0, 20, 0);
            var sobra = Texto($"Sobra {Dinheiro.Formatar(b.Sobra)}", b.Sobra < 0 ? "negativo" : "positivo"); sobra.Classes.Add("pequeno"); sobra.TextAlignment = TextAlignment.Right;
            var valorDaRenda = Texto(Dinheiro.Formatar(b.Renda), "forte"); valorDaRenda.TextAlignment = TextAlignment.Right;
            var renda = Pilha(valorDaRenda, sobra); renda.Spacing = 1; renda.VerticalAlignment = VerticalAlignment.Center; renda.Margin = new Thickness(0, 0, 16, 0); renda.MinWidth = 130;
            var editar = Botao("Editar", () => _ = Executar(() => EditarPessoa(pessoa)));
            var excluir = Botao("Excluir pessoa", () => _ = Executar(async () =>
            {
                // Não permite apagar referências utilizadas em nenhum mês, evitando despesas sem responsável.
                if (dados.Despesas.Any(e => e.Participantes.Contains(pessoa.Id))) throw new ErroDeOrcamento("Esta pessoa possui despesas no histórico. Remova ou reatribua essas despesas antes de excluí-la.");
                if (dados.Despesas.Any(e => e.PagaPor == pessoa.Id) || dados.Acertos.Any(s => s.IdDoPagador == pessoa.Id || s.IdDoRecebedor == pessoa.Id)) throw new ErroDeOrcamento("Esta pessoa aparece como quem pagou uma despesa ou em um acerto de contas. Remova esses registros antes de excluí-la.");
                if (dados.Recorrencias.Any(r => r.Participantes.Contains(pessoa.Id))) throw new ErroDeOrcamento("Esta pessoa participa de uma despesa recorrente. Em Despesas → Recorrentes, encerre e remova a recorrência antes de excluí-la.");
                if (await Confirmar(this, "Excluir pessoa?", $"Excluir {pessoa.Nome} e todos os seus registros de renda?")) Gravar(d => { d.Pessoas.RemoveAll(p => p.Id == pessoa.Id); d.Rendas.RemoveAll(i => i.IdDaPessoa == pessoa.Id); });
            }), "fantasma redondo perigo", "Lixeira", soIcone: true);
            var botoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center }; botoes.Children.Add(editar); botoes.Children.Add(excluir);
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,230,Auto,Auto"), Margin = new Thickness(0, 12) };
            Control[] celulas = [Avatar(pessoa.Nome), quem, compartilhar, renda, botoes];
            for (var i = 0; i < celulas.Length; i++) { Grid.SetColumn(celulas[i], i); fileira.Children.Add(celulas[i]); }
            fileiras.Children.Add(fileira);
        }
        painel.Children.Add(Cartao(fileiras));
        // Avisos do rateio (por exemplo, divisão igual por falta de renda disponível) aparecem uma vez cada.
        foreach (var alerta in MotorFinanceiro.Despesas(dados, mes).Select(e => MotorFinanceiro.Ratear(dados, e).Aviso).Where(w => w is not null).Distinct()) painel.Children.Add(AvisoNaPagina(alerta!));
        painel.Children.Add(Texto("Cada mês guarda sua própria renda. Alterar o nome vale para todo o histórico; alterar a renda afeta apenas este mês e recalcula a divisão dele. Na divisão proporcional, quem tem mais renda disponível assume uma parte maior.", "apagado"));
        return painel;
    }
    /// <summary>
    /// Copia rendas da competência anterior apenas para pessoas sem registro no mês selecionado, após confirmação.
    /// </summary>
    private async Task CopiarRendas()
    {
        var anteriores = dados.Rendas.Where(i => i.Mes == mes.AddMonths(-1) && !dados.Rendas.Any(atual => atual.Mes == mes && atual.IdDaPessoa == i.IdDaPessoa)).ToList();
        if (anteriores.Count == 0) throw new ErroDeOrcamento("Não há rendas anteriores para copiar. Rendas já informadas neste mês não são substituídas.");
        if (await Confirmar(this, "Copiar rendas?", $"Preencher {anteriores.Count} renda(s) ainda não informada(s) com os valores do mês anterior?")) Gravar(d => d.Rendas.AddRange(anteriores.Select(i => i with { Mes = mes, Id = Guid.NewGuid() })));
    }
    /// <summary>
    /// Abre o cadastro ou edição de pessoa. O nome é global; as rendas (uma ou mais linhas, como salário e extra) pertencem ao mês selecionado.
    /// </summary>
    private async Task EditarPessoa(Pessoa? pessoa = null)
    {
        var nome = CampoDeTexto(pessoa?.Nome, "Ex.: Ana", 80);
        // Linhas de renda em edição, com o Id das existentes preservado. Uma pessoa nova já começa com a linha "Salário".
        var linhas = new List<(Guid Id, TextBox Rotulo, TextBox Valor)>();
        if (pessoa is not null) foreach (var renda in MotorFinanceiro.LinhasDeRenda(dados, pessoa.Id, mes)) linhas.Add((renda.Id, CampoDeTexto(renda.Rotulo, "Ex.: Salário", 40), CampoDeTexto(renda.Valor.ToString("0.00", Dinheiro.Cultura), "Ex.: 3000,00", 12)));
        else linhas.Add((Guid.NewGuid(), CampoDeTexto("Salário", "Ex.: Salário", 40), CampoDeTexto("", "Ex.: 3000,00", 12)));
        var painelDeLinhas = new StackPanel { Spacing = 8 };
        // Reconstrói a lista de linhas depois de adicionar ou remover uma.
        void Atualizar()
        {
            // Solta os campos das linhas antigas: um controle só pode ter um pai.
            foreach (var antigo in painelDeLinhas.Children.OfType<Grid>()) antigo.Children.Clear();
            painelDeLinhas.Children.Clear();
            if (linhas.Count == 0) painelDeLinhas.Children.Add(Texto("Nenhuma renda neste mês: ela ficará como “não informada”.", "apagado"));
            foreach (var linha in linhas.ToList())
            {
                AutomationProperties.SetName(linha.Rotulo, "Nome da renda"); AutomationProperties.SetName(linha.Valor, "Valor da renda (R$)");
                var remover = Botao("Remover renda", () => { linhas.Remove(linha); Atualizar(); }, "fantasma redondo perigo", "Lixeira", soIcone: true);
                var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("*,170,Auto") }; linha.Rotulo.Margin = new Thickness(0, 0, 8, 0); linha.Valor.Margin = new Thickness(0, 0, 8, 0);
                Grid.SetColumn(linha.Valor, 1); Grid.SetColumn(remover, 2); fileira.Children.Add(linha.Rotulo); fileira.Children.Add(linha.Valor); fileira.Children.Add(remover); painelDeLinhas.Children.Add(fileira);
            }
        }
        Atualizar();
        var adicionar = Botao("Adicionar renda", () => { linhas.Add((Guid.NewGuid(), CampoDeTexto("", "Ex.: Salário", 40), CampoDeTexto("", "Ex.: 3000,00", 12))); Atualizar(); }, icone: "Mais"); adicionar.HorizontalAlignment = HorizontalAlignment.Left;
        var erro = Texto("", "erro");
        var painel = Pilha(Cabecalho(pessoa is null ? "Adicionar pessoa" : "Editar pessoa", $"Rendas de {mes.ToString("MMMM 'de' yyyy", Dinheiro.Cultura)}"), Campo("Nome", nome),
            Texto("Rendas do mês (líquidas)", "secao"), Texto("Use uma linha para cada fonte: salário, extra, 13º. Use vírgula para centavos; 0 é válido. A renda do mês é a soma das linhas.", "apagado"), painelDeLinhas, adicionar, erro);
        var dialogo = Dialogo("Pessoa e renda", painel); dialogo.Height = 680;
        painel.Children.Add(Linha(Botao("Cancelar", () => dialogo.Close()), Botao("Salvar pessoa", () =>
        {
            try
            {
                var editada = new Pessoa { Id = pessoa?.Id ?? Guid.NewGuid(), Nome = nome.Text?.Trim() ?? "" };
                var rendas = linhas.Select(l => new RendaMensal { Id = l.Id, IdDaPessoa = editada.Id, Mes = mes, Rotulo = l.Rotulo.Text?.Trim() ?? "", Valor = Dinheiro.Interpretar(l.Valor.Text, "Renda") }).ToList();
                Gravar(d =>
                {
                    if (pessoa is null) d.Pessoas.Add(editada); else d.Pessoas[d.Pessoas.FindIndex(p => p.Id == pessoa.Id)] = editada;
                    // Substitui somente as linhas desta pessoa neste mês, preservando as outras competências.
                    d.Rendas.RemoveAll(i => i.IdDaPessoa == editada.Id && i.Mes == mes);
                    d.Rendas.AddRange(rendas);
                }); dialogo.Close();
            }
            catch (Exception ex) { erro.Text = ErroAmigavel(ex); erro.BringIntoView(); }
        }, "primario")));
        await dialogo.ShowDialog(this);
    }
}
