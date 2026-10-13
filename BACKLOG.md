# MTG Estoque — o que falta melhorar

Levantado numa revisão de UI/UX (out/2026). Já resolvido nessa rodada: retorno único (aviso
embaixo com Desfazer), painel da direita só com preview, confirmações no estilo do app (sem
caixa do Windows), "Tirar do deck" separado de "Remover da coleção", bolinhas de status no deck,
campos de adicionar/filtrar separados.

## Prioridade alta

- **Primeiro uso / estado vazio.** Com o app vazio, o Início mostra duas seções sem nada. Trocar
  por 3 ações grandes: *Importar coleção (CSV do Moxfield)*, *Novo deck por link*, *Criar binder*.
- **Backup.** Todo o estoque mora num único `%APPDATA%\MtgEstoque\data.json`. Ter *Exportar
  backup* / *Restaurar* no Início e um backup automático (ex.: um por dia, mantendo os últimos 7).
- **Paginação atrapalha arrastar em binders grandes.** Não dá pra arrastar da página 1 pra uma
  carta da página 3 (os chips de setor contornam). Como a lista já é virtualizada, dá pra tirar a
  paginação dos binders e mostrar tudo.

## Prioridade média — feito

Atalhos (Ctrl+F, Ctrl+Z, Esc, Shift+clique, Ctrl+A), busca do Início levando pro Geral filtrado,
janela com tamanho mínimo 1000×640, ⋮ também em binders/boxes, bolinhas em binders (amarelo = um
deck precisa dessa carta) e no Geral (azul = tem livre, cinza = tudo em decks), e a aba
**Faltando nos decks** no Geral (want list de todos os decks).

## Prioridade baixa / acabamento

- **Temas escuros — controles nativos.** A alça da barra de rolagem, as caixas de seleção (ComboBox)
  e a caixinha do CheckBox ainda usam o desenho padrão do Windows (claro). Dá pra fazer templates
  próprios pra ficarem no tom do tema.

- **Contagem do deck sem referência.** "103 cartas" não diz se está certo pra Commander. Mostrar
  "103/100" em amarelo quando passar (ou faltar).
- **Status só por cor na quantidade** — hoje a bolinha tem tooltip; garantir que todo sinal de cor
  tenha também forma ou texto no hover (acessibilidade).
- **Lembrar preferências entre aberturas:** largura do painel da imagem, seções recolhidas da barra
  lateral, último deck aberto.
- **Painel de preview vazio** mostra "Passe o mouse numa carta"; poderia mostrar a capa do deck ou
  um resumo (cartas, faltantes, valor).
- **Etched** é tratado como foil comum; se importar, guardar o acabamento separado.
- **Cache de artes não expira** (`img\arts3-*.json`): sets novos só aparecem apagando a pasta.
  Adicionar validade (ex.: 30 dias) ou botão "atualizar artes".
- **Importação por link depende da API interna do Moxfield** (não oficial). Se quebrar, cair pro
  "cole a lista" com uma mensagem clara.
