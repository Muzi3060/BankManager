using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using BankManager.Nucleo;
using static BankManager.Componentes;

namespace BankManager;
// Página de ajustes: aparência, backup e recuperação dos dados e textos de ajuda.
public sealed partial class JanelaPrincipal
{
    /// <summary>
    /// Ajustes: escolha do tema (cartões com miniatura), seus dados (pasta, backup, recuperação) e ajuda em seções que abrem e fecham.
    /// </summary>
    private Control PaginaDeAjustes()
    {
        var temas = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*,12,*") };
        Control[] cartoes = [CartaoDeTema("Claro", "Fundo claro", PreferenciaDeTema.Claro, MiniaturaDoTema(ThemeVariant.Light)), CartaoDeTema("Escuro", "Fundo escuro", PreferenciaDeTema.Escuro, MiniaturaDoTema(ThemeVariant.Dark)), CartaoDeTema("Automático", "Segue o sistema", PreferenciaDeTema.Automatico, MiniaturaDoTemaAutomatico())];
        for (var i = 0; i < cartoes.Length; i++) { Grid.SetColumn(cartoes[i], i * 2); temas.Children.Add(cartoes[i]); }
        var aparencia = Cartao(Pilha(Texto("Aparência", "section"), Texto("Escolha como o aplicativo aparece. A opção automática acompanha o tema do seu computador.", "muted"), temas));
        var quebra = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var botao in new[] { Botao("Exportar backup", () => _ = Executar(Exportar), "ink"), Botao("Importar backup", () => _ = Executar(Importar)), Botao("Recuperar versão anterior", () => _ = Executar(Recuperar)) }) { botao.Margin = new Thickness(0, 0, 8, 8); quebra.Children.Add(botao); }
        var pastaDosDados = Texto(armazenamento?.CaminhoDaPasta ?? "—", "muted"); pastaDosDados.FontSize = 12;
        // Planilhas: exportar as despesas e importar um extrato bancário, ambos em CSV.
        var planilhas = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var botao in new[] { Botao("Exportar despesas (CSV)", () => _ = Executar(ExportarCsv)), Botao("Importar extrato (CSV)", () => _ = Executar(ImportarExtrato)), Botao("Compartilhar despesas", () => { IrPara(2); IniciarSelecao(); }) }) { botao.Margin = new Thickness(0, 0, 8, 8); planilhas.Children.Add(botao); }
        var importarCompartilhadas = Botao("Importar despesas compartilhadas", () => _ = Executar(ImportarCompartilhadas), icone: "Download"); importarCompartilhadas.HorizontalAlignment = HorizontalAlignment.Left;
        var seusDados = Cartao(Pilha(Texto("Seus dados", "section"), Texto("Tudo fica salvo só neste computador. Exporte um backup de vez em quando e guarde em outro lugar, como um pendrive.", "muted"), Texto("Pasta dos dados", "muted"), pastaDosDados, quebra,
            // Separado do backup de propósito: importar um backup SUBSTITUI a base; importar despesas compartilhadas só ADICIONA.
            Texto("Despesas recebidas de outra pessoa", "muted"), importarCompartilhadas,
            Texto("Adiciona as despesas de um arquivo de compartilhamento às suas, sem apagar nem trocar nada. Diferente de “Importar backup”, que substitui tudo.", "muted").ComClasse("small"),
            Texto("Planilhas e compartilhamento", "muted"), planilhas,
            Texto("Compartilhar abre a lista de despesas para você marcar as que quer enviar; o arquivo leva só as marcadas.", "muted").ComClasse("small")));
        var ajuda = Cartao(Pilha(Texto("Ajuda", "section"),
            Recolhivel("Como funciona a divisão", "Individual: uma pessoa paga tudo.\nIgual: o valor é dividido igualmente entre os selecionados.\nProporcional: quem tem mais renda disponível assume uma parcela maior. Despesas individuais, pagas ou em aberto, são descontadas da renda antes da divisão. Todas as despesas proporcionais do mês usam essa mesma base.\n\nExemplo: rendas disponíveis de R$ 3.000 e R$ 1.000 dividem uma conta de R$ 800 em R$ 600 e R$ 200. Quem não tem renda disponível recebe peso zero. Se todos tiverem peso zero, a conta é dividida igualmente, com aviso. Centavos restantes são distribuídos sem alterar o total."),
            Divisoria(),
            Recolhivel("Dívidas, parcelas e listas", "Para uma dívida, informe o valor total já com os juros negociados e o número de parcelas. A primeira data é o primeiro vencimento. O aplicativo distribui o total; não calcula taxas bancárias.\n\nUma lista soma quantidade × preço de cada item. Não cadastre a mesma compra novamente como outra despesa. Marcar como pago quita o lançamento inteiro para todos os responsáveis e guarda a data do pagamento (hoje, ou a que você informar); pagamentos parciais não são registrados nesta versão. Em “Últimas movimentações”, a despesa aparece no mês em que foi paga; as pagas em versões anteriores, sem essa data, aparecem pelo vencimento.\n\nO saldo planejado considera todas as despesas do mês, inclusive as pagas. Parcelas são editadas e excluídas individualmente."),
            Divisoria(),
            Recolhivel("Privacidade e backups", "O aplicativo funciona sem internet e não envia nenhum dado para fora. Cada salvamento guarda uma cópia da versão anterior, que você pode recuperar aqui ao lado.\n\nOs arquivos não são criptografados: backups contêm informações financeiras em texto legível. Guarde-os em um local privado e não os envie a quem você não conhece.")));
        ((StackPanel)ajuda.Child!).Spacing = 4;
        var abaixo = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*") }; Grid.SetColumn(ajuda, 2); abaixo.Children.Add(seusDados); abaixo.Children.Add(ajuda); seusDados.VerticalAlignment = VerticalAlignment.Top; ajuda.VerticalAlignment = VerticalAlignment.Top;
        var pagina = Pilha(CabecalhoDaPagina("Ajustes", "Aparência, categorias, seus dados e ajuda.", navegacaoDeMes: false), aparencia, CartaoDeCategorias(), abaixo); pagina.Spacing = 18; return pagina;
    }
    /// <summary>
    /// Cartão clicável de tema: miniatura, nome e destaque na opção atual. Clicar aplica o tema na hora.
    /// </summary>
    private Control CartaoDeTema(string titulo, string subtitulo, PreferenciaDeTema valorLido, Control previa)
    {
        var rotulo = Pilha(new TextBlock { Text = titulo, FontWeight = FontWeight.Medium }, Texto(subtitulo, "muted")); rotulo.Spacing = 0;
        var cartao = new Button { Content = Pilha(previa, rotulo) }; cartao.Classes.Add("themecard"); cartao.Classes.Set("selected", preferencias.Tema == valorLido);
        ((StackPanel)cartao.Content).Spacing = 10;
        AutomationProperties.SetName(cartao, "Tema " + titulo.ToLowerInvariant());
        cartao.Click += (_, _) => { MudarTema(valorLido); Desenhar(); };
        return cartao;
    }
    /// <summary>
    /// Miniatura de uma tela (menu, cartão translúcido e cartão escuro sobre o degradê) desenhada no tema pedido, independentemente do tema atual.
    /// </summary>
    private static Control MiniaturaDoTema(ThemeVariant variante) => new ThemeVariantScope { RequestedThemeVariant = variante, Child = new Border { Child = TelaDaMiniatura(true), Height = 76 }.ComClasse("mini-frame") };
    /// <summary>
    /// Conteúdo da miniatura: fundo em degradê, barras do menu (opcionais) e dois cartões.
    /// </summary>
    private static Control TelaDaMiniatura(bool menu)
    {
        var barras = new StackPanel { Spacing = 4, Margin = new Thickness(7, 9, 0, 0), Width = 22 }; foreach (var largura in new[] { 16d, 12d, 14d }) barras.Children.Add(BarraDaMiniatura("mini-muted", largura));
        var cartao = new Border { Child = Pilha(BarraDaMiniatura("mini-text", 26), BarraDaMiniatura("mini-accent", 18)), Padding = new Thickness(6), Margin = new Thickness(0, 0, 5, 0) }.ComClasse("mini-card"); ((StackPanel)cartao.Child!).Spacing = 5;
        var escuro = new Border().ComClasse("mini-dark"); Grid.SetColumn(escuro, 1);
        var cartoes = new Grid { ColumnDefinitions = new ColumnDefinitions("1.7*,*"), Margin = new Thickness(8, 10, 8, 10), Children = { cartao, escuro } }; Grid.SetColumn(cartoes, 1);
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions(menu ? "Auto,*" : "0,*"), Children = { barras, cartoes } }; barras.IsVisible = menu;
        return new Border { Child = grade }.ComClasse("mini-bg");
    }
    /// <summary>
    /// Miniatura do modo automático: metade clara, metade escura.
    /// </summary>
    private static Control MiniaturaDoTemaAutomatico()
    {
        var esquerda = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = TelaDaMiniatura(true) };
        var direita = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = TelaDaMiniatura(false) };
        var grade = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") }; Grid.SetColumn(direita, 1); grade.Children.Add(esquerda); grade.Children.Add(direita);
        return new Border { Child = grade, Height = 76 }.ComClasse("mini-frame");
    }
    /// <summary>
    /// Barra arredondada da miniatura; a cor vem da classe ("mini-text", "mini-muted" ou "mini-accent").
    /// </summary>
    private static Border BarraDaMiniatura(string estilo, double largura) { var barra = new Border { Width = largura }; barra.Classes.Add(estilo); return barra; }
    /// <summary>
    /// Cartão de categorias: as padrão (fixas) e as suas, que podem ser criadas, renomeadas e excluídas.
    /// </summary>
    private Control CartaoDeCategorias()
    {
        var padrao = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var nome in RegrasDoOrcamento.Categorias) { var etiqueta = Etiqueta(nome, "status"); etiqueta.Margin = new Thickness(0, 0, 6, 6); padrao.Children.Add(etiqueta); }
        var proprias = new StackPanel();
        foreach (var nome in dados.CategoriasProprias)
        {
            if (proprias.Children.Count > 0) proprias.Children.Add(Divisoria());
            var rotulo = new TextBlock { Text = nome, VerticalAlignment = VerticalAlignment.Center };
            var renomear = Botao("Renomear " + nome, () => _ = Executar(async () =>
            {
                var renomeado = await Perguntar(this, "Renomear categoria", "Novo nome", nome, MotorDeCategorias.TamanhoMaximo);
                if (renomeado is not null) Gravar(d => MotorDeCategorias.Renomear(d, nome, renomeado));
            }), "ghost round", "Edit", soIcone: true);
            var remover = Botao("Excluir " + nome, () => _ = Executar(async () =>
            {
                if (await Confirmar(this, "Excluir categoria?", $"Excluir a categoria “{nome}”? Só é possível se nenhuma despesa ou recorrência a usar.")) Gravar(d => MotorDeCategorias.Remover(d, nome));
            }), "ghost round danger", "Trash", soIcone: true);
            var fileira = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 4) };
            Grid.SetColumn(renomear, 1); Grid.SetColumn(remover, 2); fileira.Children.Add(rotulo); fileira.Children.Add(renomear); fileira.Children.Add(remover); proprias.Children.Add(fileira);
        }
        if (dados.CategoriasProprias.Count == 0) proprias.Children.Add(Texto("Você ainda não criou categorias próprias.", "muted"));
        var entrada = CampoDeTexto(null, "Nova categoria", MotorDeCategorias.TamanhoMaximo); AutomationProperties.SetName(entrada, "Nova categoria");
        var adicionar = Botao("Adicionar categoria", () => _ = Executar(() => { Gravar(d => MotorDeCategorias.Adicionar(d, entrada.Text ?? "")); return Task.CompletedTask; }), icone: "Plus");
        var formulario = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,Auto") }; Grid.SetColumn(adicionar, 2); formulario.Children.Add(entrada); formulario.Children.Add(adicionar);
        return Cartao(Pilha(Texto("Categorias", "section"), Texto("As categorias padrão não mudam. Crie as suas para organizar melhor, por exemplo Pets ou Viagens.", "muted"), padrao, proprias, formulario));
    }
    /// <summary>
    /// Aplica o tema na hora e o guarda em settings.json. Se o disco falhar, o tema vale na sessão e o usuário é avisado.
    /// </summary>
    private void MudarTema(PreferenciaDeTema preferencia)
    {
        if (preferencia == preferencias.Tema) return;
        preferencias = preferencias with { Tema = preferencia }; Aplicativo.AplicarTema(preferencia);
        try { EsconderAviso(); ArmazenamentoDePreferencias.Salvar(pastaDasPreferencias, preferencias); }
        catch (Exception ex) { MostrarErro(ex is IOException or UnauthorizedAccessException ? new ErroDeOrcamento("O tema foi trocado, mas não foi possível guardar a escolha. Ela volta ao padrão na próxima abertura.") : ex); }
    }
    /// <summary>
    /// Solicita um arquivo local e grava uma cópia validada dos dados atuais fora da pasta interna.
    /// </summary>
    private async Task Exportar()
    {
        var arquivo = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Exportar backup", SuggestedFileName = $"bankmanager-{DateTime.Today:yyyy-MM-dd}.json", DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("Backup JSON") { Patterns = ["*.json"] }] });
        // Fechar o seletor de arquivo equivale a cancelar; nada é gravado.
        if (arquivo is null) return;
        var caminho = arquivo.TryGetLocalPath() ?? throw new ErroDeOrcamento("Escolha uma pasta local.");
        RecusarCaminhoInterno(caminho); ArmazenamentoLocal.Exportar(caminho, dados); Avisar("Backup exportado com sucesso.", sucesso: true);
    }
    /// <summary>
    /// Rejeita arquivos diretamente na pasta interna para proteger a base, o backup e o bloqueio de acesso.
    /// </summary>
    private void RecusarCaminhoInterno(string caminho)
    {
        if (armazenamento is not null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(caminho)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(armazenamento.CaminhoDaPasta)), StringComparison.OrdinalIgnoreCase))
            throw new ErroDeOrcamento("Escolha um arquivo fora da pasta interna do aplicativo.");
    }
    /// <summary>
    /// Valida o backup selecionado antes da confirmação e substitui o estado somente depois de salvar com sucesso.
    /// </summary>
    private async Task Importar()
    {
        if (armazenamento is null) throw new ErroDeOrcamento("Feche outra instância do aplicativo antes de importar dados.");
        var arquivos = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Importar backup", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("Backup JSON") { Patterns = ["*.json"] }] });
        if (arquivos.Count == 0) return;
        var caminho = arquivos[0].TryGetLocalPath() ?? throw new ErroDeOrcamento("Escolha um arquivo local.");
        // Lê e valida todo o arquivo antes de pedir autorização para substituir o histórico.
        RecusarCaminhoInterno(caminho); var importado = ArmazenamentoLocal.Ler(caminho);
        if (!await Confirmar(this, "Substituir os dados atuais?", $"Backup validado: {importado.Pessoas.Count} pessoa(s), {importado.Despesas.Count} despesa(s). A importação substitui todo o histórico atual. A versão anterior fica na cópia automática.")) return;
        armazenamento.Salvar(importado); dados = importado; pronto = true; CompletarRecorrencias(); Desenhar();
    }
    /// <summary>
    /// Lê a cópia automática, confirma a substituição e salva seu conteúdo como base atual antes de atualizar a tela.
    /// </summary>
    private async Task Recuperar()
    {
        if (armazenamento is null || !File.Exists(armazenamento.CaminhoDaCopia)) throw new ErroDeOrcamento("Não há cópia automática disponível. Você pode importar um backup exportado anteriormente.");
        var backup = ArmazenamentoLocal.Ler(armazenamento.CaminhoDaCopia);
        if (!await Confirmar(this, "Recuperar versão anterior?", $"Restaurar {backup.Pessoas.Count} pessoa(s) e {backup.Despesas.Count} despesa(s)? Os dados atuais serão substituídos.")) return;
        // Save mantém a base substituída como cópia anterior; a recuperação também segue a gravação normal.
        armazenamento.Salvar(backup); dados = backup; pronto = true; CompletarRecorrencias(); Desenhar();
    }
}
