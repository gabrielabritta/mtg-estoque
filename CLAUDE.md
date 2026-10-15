# MTG Estoque — contexto do projeto

App desktop Windows (WPF, .NET 10, C#) para gerenciar coleção de Magic **por cópia física**: cada carta
está em exatamente um lugar (deck, binder, box). Uso pessoal do Gabriel (+ um amigo testando via instalador).
UI e textos em **português**; 100% local, sem login/servidor. Estilo de código: mínimo, sem abstrações
desnecessárias (modo "ponytail"); comentários curtos em pt-BR explicando o porquê.

## Build / rodar / testar

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist   # dev (atalho na área de trabalho aponta pra dist\MtgEstoque.exe)
dist\MtgEstoque.exe --test        # self-test (Store.SelfTest + Types.LigaTest + Liga.SelfTest); exit 0 = ok
```
- Sempre rodar o `--test` depois de mexer em lógica; o app precisa estar fechado pra publicar (`Stop-Process MtgEstoque`).
- **Instalador** (só quando o Gabriel pedir): publish autocontido em `publish\` e Inno Setup:
  ```
  dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer.iss    # -> installer\MTG-Estoque-Setup-<versão>.exe (~57 MB)
  ```
  - **Nova versão**: subir `<Version>` no csproj **e** `AppVersion` no installer.iss (iguais), gerar, `gh release create vX.Y installer\MTG-Estoque-Setup-X.Y.exe installer\mtg-estoque-chrome-extension.zip` — os apps instalados avisam sozinhos. Repo público: github.com/gabrielabritta/mtg-estoque.
  Instala por usuário (sem admin) e pergunta a pasta de dados → grava `datadir.txt` ao lado do exe.
- Flags de diagnóstico (não mexem nos dados): `--parse <arq> <saida>`, `--mox <url> <saida>`,
  `--liga <deck> <saida>` (export LigaMagic), `--ligaresolve <compra.txt> <saida>` (identifica artes).

## Arquivos

| Arquivo | O quê |
|---|---|
| `Store.cs` | Modelo + regras (Data, Location, Stack, Want, Moved), parse de listas (Moxfield texto/CSV), formatos de export, migrações, self-test |
| `MainWindow.xaml(.cs)` | Janela principal (sem barra do Windows: WindowChrome + botões próprios); todas as telas e ações |
| `Dialogs.cs` | Diálogos (lista, revisão "de onde vêm as cartas", mover, troca de arte, artes, excluir, confirmação…) |
| `Theme.cs` | 9 temas (paletas), `Theme.B("Chave")`, picker de tema, `settings.json` |
| `Img.cs` | Scryfall: links de imagem em lote (`/cards/collection`), download pela CDN com cache, feed de impressões paginado, autocomplete |
| `Types.cs` | type_line (agrupamento Criatura/Feitiço…) e layout (dupla-face vs duas metades) com cache |
| `Moxfield.cs` | Import de deck por link (API interna `api2.moxfield.com/v3/decks/all/{id}`; fallback `curl.exe` se o HttpClient levar 403) |
| `Liga.cs` | Import de compras da LigaMagic + diálogo de conferência |
| `chrome-extension/` | Extensão pessoal do Chrome (MV3, sem compactação) "Copiar compras (MTG Estoque)" — ver abaixo |
| `installer.iss` | Inno Setup (atualização: pula a página de dados se já existe `datadir.txt`) |
| `Update.cs` | Ao abrir, consulta a última release no GitHub (API pública) e oferece baixar/rodar o Setup `/SILENT` |
| `BACKLOG.md` | Pendências priorizadas da revisão de UI/UX |

## Modelo de dados (`%APPDATA%\MtgEstoque\data.json`, ou pasta do `datadir.txt`)

- `Locations`: `{Name, Kind: deck|binder|box, Wants[], Source?, Cover?, Sectors[]}`.
  - `Want {Card, Qty, Board: Comandante|Main|Sideboard|Considerando, Print}` — **o que o deck pede**; `Print` = arte desejada ("" = qualquer).
- `Stacks`: cópias físicas `{Card, Print "set/num", Foil, Qty, Loc, Sector}`.
- `Prints`: arte exibida/preferida por carta (fallback). `Log`: movimentações (máx. 5000). `DefaultLoc`: binder/box padrão.
- Falta no deck = Wanted(sem Considerando) − cópias no deck. Livre = cópias fora de decks.
- **Local padrão**: `Store.Default(d)` = `DefaultLoc` ou primeiro binder/box; se não houver, cria "Avulsas". (Versões antigas tinham um local "global" — "Caixa global"/"cartinhas" — migrado no `FromJson`.)
- `Store.Move(..., keepWants)`: mover tira da lista do deck de origem e entra na do destino (sem "rastro"); `keepWants: true` só move a cópia física (troca de arte, sobras, exclusão).
- Desfazer: `Store.Snapshot(d)` (JSON inteiro) antes da ação → `ShowToast(msg, snap)`; qualquer `Commit()` fecha o aviso.
- Outros arquivos na pasta: `settings.json` (tema, boas-vindas vista), `types.json`, `layouts.json`, `uris.json`, `img\` (cache de imagens e listas de artes `arts3-*.json`).

## Regras de produto já decididas (não reverter sem perguntar)

- Importar/sincronizar deck → **tela de revisão** por carta (usar da coleção / tirar de outro deck / criar cópia / faltando), com seletor de arte. Usou da coleção → a arte da cópia vira a do deck (`Store.AcceptOwnedArt`).
- Arte: **"Corrigir esta arte"** (cadastro errado, nada se move) × **"Alterar arte"** (grava arte desejada; se tem livre troca na hora; senão fica "quer X").
- Deck em colunas (masonry por ordem: Comandante, Batalha, Planeswalker, Criatura, Instantâneo, Feitiço, Encantamento, Artefato, Terreno, Outros); Sideboard/Considerando numa seção separada embaixo.
- Bolinhas: azul = tem a cópia; azul vazado = tem em outra arte; vermelho = falta; amarelo = tem mas em uso em outro deck; verde (Considerando) = tem livre; tooltip explica. Foil = ✦ dourado (`Theme.B("Foil")`). Código da arte só no painel lateral.
- Excluir local pergunta: desmontei (mover p/ binder escolhido) ou vendi (remover da coleção).
- Exports: Moxfield por seção (Main, Sideboard, Considerando, Main+Side, completo com cabeçalhos); LigaMagic sem arte, dupla-face só a frente, split/adventure/room com " // ", inclui sideboard (sem Considerando).
- Cores sempre via chaves de tema (`Bg, Surface, Hover, Selected, Border, Text, Muted, Accent, OnAccent, Chip, Input, Toast, OnToast, DangerText, Dot*, Good, Warn, Foil…`). Não usar hex fixo na UI. Estilos de botão: `Primary`, `Danger` (nomes de estilo ≠ nomes de pincel!).
- Não gerar instalador a cada mudança — só quando pedirem.

## LigaMagic (compras)

- Página "Meus Pedidos": `https://www.ligamagic.com.br/?view=mp/compras`. Itens só existem no DOM depois de clicar "Visualizar Itens" (carregados por `POST /ajax/mp/compras_itens.php`; recarregar a página fecha).
- Cada item tem link `?view=cards/card&card=<nome EN>&ed=<edição Liga>&num=` (num vem vazio), texto com Foil/idioma/condição/"N unid.", e scan em `repositorio.sbrauble.com/arquivos/in/magic/...jpg`.
- Edição da Liga = código Scryfall com prefixo de variante (`bfcmm`→cmm, `cbcmr`→cmr, `ofmh2`→mh2) ou letra extra (`pwp22`→pw22): `Liga.SetGuesses` tenta sufixos e depois remoção de 1 letra.
- Várias impressões na edição → compara o scan com cada impressão: miniatura **12×17 em cinza da carta inteira** (separa arte estendida/borderless da comum; idioma não atrapalha). Certeza: melhor < 15 e 2º − melhor > 6. Testado: compra #MP1321868, 28/28 certas.
- Extensão: clique no ícone em Meus Pedidos → copia **só as compras abertas** (`#venda_<número>` visível; o usuário abre com "Visualizar Itens"/"Itens"), espera "N unid." e copia. "Visualizar Pedido" = compra no site da loja (fora da Liga). Guia velha → ajax responde "precisa estar logado" (token expirou): F5.
- Formato copiado pela extensão: `#ligamagic\n qtd⇥nome⇥ed⇥foil⇥img` → colar em "Adicionar lista" de um binder/box.
- Clipboard de página só funciona com gesto do usuário (por isso testar via `javascript_tool` não copia). Extensão `claude-in-chrome` não abre `file://`.

## Pegadinhas do ambiente (Windows / PowerShell 5.1)

- **O shell do Claude desktop é sandbox MSIX**: `%APPDATA%` lido/escrito por ele (e pelo app aberto por ele) vai para `AppData\Local\Packages\Claude_*\LocalCache\Roaming`, não o AppData real. Por isso `dist\datadir.txt` aponta para `Documents\MtgEstoque` (fora do AppData, mesma pasta vista pelos dois lados).

- PS 5.1 lê arquivos como ANSI e estraga acentos: editar com a ferramenta Edit ou `[IO.File]::ReadAllText/WriteAllText` com UTF8 sem BOM. Heredocs no Bash quebram com aspas: usar script Python em arquivo.
- Scryfall: API ~10 req/s (gate de 100 ms em `Img.Fetch`/`PostJson`); imagens pela CDN sem limite. `/cards/collection` **só acha cartas de duas partes pelo nome da frente**.
- Moxfield bloqueia o HttpClient às vezes (Cloudflare) → fallback `curl.exe`.
- Janela sem barra do Windows: `WM_GETMINMAXINFO` limita maximizar à área útil; `Root.Margin` compensa a moldura; `KeepOnScreen` traz a janela pra dentro.
