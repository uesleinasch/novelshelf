# NovelShelf

Leia web novels, **mangás, manhwas, manhuas e HQs** no seu **Kindle**, sem jailbreak, sem servidor na nuvem e sem Calibre.

O NovelShelf é um app desktop (Linux e Windows) que busca obras na web, baixa os capítulos para uma biblioteca no seu PC, monta **um livro por obra** e envia o livro para o Kindle pelo cabo USB. Quando saem capítulos novos, você baixa e reenvia: o livro no Kindle é **substituído**, sem criar cópias, e a leitura continua de onde parou.

> - **Novels:** [Novel Mania](https://novelmania.com.br) e [Illusia](https://illusia.com.br).
> - **Mangás, manhwas, manhuas e HQs:** mais de 160 sites em português pelas extensões do Mihon mantidas pelo [Project Nox](https://github.com/ProjectNox-org/manga-extensoes). As páginas são cortadas e ajustadas à tela do Kindle.

---

## Sumário

- [Por que existe](#por-que-existe)
- [Como funciona](#como-funciona)
- [Instalação](#instalação)
- [Uso](#uso)
- [Mangás, manhwas e HQs](#mangás-manhwas-e-hqs)
- [Kindle: detalhes importantes](#kindle-detalhes-importantes)
- [Arquitetura](#arquitetura)
- [Rodar a partir do código](#rodar-a-partir-do-código)
- [Uso responsável](#uso-responsável)
- [Limitações conhecidas](#limitações-conhecidas)
- [Roadmap](#roadmap)
- [Contribuir](#contribuir)
- [Licença e créditos](#licença-e-créditos)

---

## Por que existe

Ler novel no navegador do Kindle é lento e cansativo. O caminho comum é baixar capítulo por capítulo, converter no Calibre e copiar para o Kindle. Isso vira trabalho manual a cada capítulo novo, e ainda deixa livros duplicados no aparelho.

O NovelShelf automatiza esse fluxo inteiro:

1. **Buscar** a novel pelo nome ou colar o link.
2. **Escolher quantos capítulos** baixar.
3. **Enviar para o Kindle** com um clique.

## Como funciona

```
 Novel Mania ──HTML──▶ Fonte (parser) ──▶ Biblioteca local (SQLite)
                                              │
                                              ▼
                                    Gerador de livro
                                   ├── EPUB  (guardado no PC)
                                   └── AZW3  (formato do Kindle)
                                              │
                                     cabo USB │ (disco ou MTP)
                                              ▼
                              Kindle: documents/NovelShelf/<novel>.azw3
```

- **Biblioteca incremental.** Cada capítulo baixado fica salvo no PC. O próximo download continua do último capítulo salvo, seguindo o link "próximo capítulo" do site. Isso também pega capítulos lançados depois.
- **Um livro por novel.** O livro é sempre gerado de novo com **todos** os capítulos salvos, mantendo o mesmo identificador (UUID/ASIN) e o mesmo nome de arquivo.
- **AZW3 nativo.** O Kindle não abre EPUB enviado pelo cabo, só pelo e-mail da Amazon. Por isso o app gera AZW3 (KF8) com um escritor próprio em C#, sem depender do Calibre.
- **Envio igual ao do Calibre.** O app detecta o Kindle conectado, copia o livro para `documents/NovelShelf/` e substitui a versão anterior. A capa vai para `system/thumbnails` para aparecer na biblioteca do Kindle.

## Instalação

Baixe a última versão em **[Releases](../../releases)**:

| Sistema | Arquivo | Como abrir |
|---|---|---|
| Linux x64 (AppImage) | `NovelShelf-vX.Y.Z-x86_64.AppImage` | `chmod +x` e dê dois cliques (ou `./NovelShelf-vX.Y.Z-x86_64.AppImage`) |
| Linux x64 | `NovelShelf-vX.Y.Z-linux-x64.tar.gz` | extraia e rode `./NovelShelf` |
| Windows x64 | `NovelShelf-vX.Y.Z-win-x64.zip` | extraia e rode `NovelShelf.exe` |

Os executáveis já trazem o runtime do .NET. Não é preciso instalar nada.

## Uso

1. **Buscar novels.** Digite o nome ou cole o link da novel (ou de um capítulo) e clique na novel para ver sinopse, gêneros e total de capítulos.
2. **Adicionar à biblioteca.** A novel vai para a aba **Biblioteca**.
3. **Baixar capítulos.** Escolha quantos capítulos baixar (padrão: 50) e clique em **Baixar**. Dá para cancelar; o que já baixou fica salvo.
4. **Enviar para o Kindle.** Conecte o Kindle no USB, desbloqueie a tela e clique em **Enviar**. O app gera o livro e copia para o aparelho.
5. **Ejetar.** Clique em **Ejetar**, na biblioteca ou na página Kindle, e desconecte o cabo. Só depois de ejetar o Kindle atualiza a biblioteca.
   - **Linux:** usa o `gio`, como o gerenciador de arquivos (disco USB e MTP).
   - **Windows:** usa o "Ejetar" do Explorer.

Na **Biblioteca**, os botões no canto superior direito trocam entre **lista** e **grade de capas**; o app lembra a escolha. Clique com o botão direito numa obra para baixar, enviar, gerar o livro ou remover sem abrir os detalhes.

Acima da lista ficam a busca por título ou autor e os filtros: tipo (novel ou quadrinho), site de origem, situação no Kindle (novos para enviar, em dia, nunca enviados) e a ordenação (atualizados, título, adicionados).

**Estilo do livro.** Na página **Kindle**, em *Estilo dos livros*, você ajusta o recuo da primeira linha, o espaço entre parágrafos, o alinhamento (justificado ou à esquerda) e o título dos capítulos (como no site, só "Capítulo N" ou sem título). A prévia mostra o resultado na hora. Cada novel pode ter um estilo próprio: botão **Estilo do livro** nos detalhes dela. Ao mudar o estilo de uma obra já enviada, ela aparece como "Ajustes novos para o Kindle"; basta enviar de novo. Tamanho e tipo da letra continuam no menu *Aa* do próprio Kindle.

A lista mostra, para cada novel, quantos capítulos estão no PC e quantos já estão no Kindle. Por exemplo: *"Kindle tem 120 capítulos — 15 novos para enviar"*.

O botão **Gerar livro** cria o EPUB e o AZW3 sem enviar, para usar em outros leitores. **Abrir pasta** mostra onde os arquivos ficam.

### Onde ficam os dados

| Sistema | Pasta |
|---|---|
| Linux | `~/.local/share/NovelShelf/` |
| Windows | `%LOCALAPPDATA%\NovelShelf\` |

Dentro dela:

| Item | O que guarda |
|---|---|
| `library.db` | a biblioteca |
| `books/` | EPUB e AZW3 gerados |
| `covers/` | capas |
| `comics/` | páginas originais dos quadrinhos baixados |
| `suwayomi/` | Java, servidor de mangás e as extensões instaladas |
| `settings.json` | modelo do Kindle e preferências |

## Mangás, manhwas e HQs

Os sites de quadrinhos vêm das **extensões do Mihon** (o leitor de mangá do Android) mantidas pela comunidade [Project Nox](https://github.com/ProjectNox-org/manga-extensoes): mais de 160 fontes em português. O NovelShelf não reescreve essas fontes. Ele roda as extensões originais num servidor local, o [Suwayomi-Server](https://github.com/Suwayomi/Suwayomi-Server), e conversa com ele por uma API.

```
 Site ──▶ extensão do Mihon ──▶ Suwayomi (local, 127.0.0.1) ──GraphQL──▶ NovelShelf
                                                                          │
                                         corta, divide e ajusta as páginas │
                                                                          ▼
                                                     AZW3 de layout fixo ──▶ Kindle
```

### Como usar

1. Abra **Mangás e HQs** e clique em **Baixar e ativar**. Isso só acontece uma vez.
   - O app baixa o Java 21 (Temurin) e o Suwayomi, cerca de 235 MB, e confere o SHA-256 de cada arquivo.
2. Clique em **Fontes** e instale os sites que você usa. A lista mostra fontes em português.
   - Fontes só para adultos ficam escondidas até você marcar **Mostrar fontes adultas (+18)**.
3. Escolha a fonte e use **Buscar**, **Explorar** (populares) ou **Novidades**. A consulta vai ao site na hora.
4. Clique numa obra e em **Adicionar à biblioteca**.
5. Na **Biblioteca**:
   1. Use **Baixar** para trazer as páginas dos próximos capítulos para o PC.
   2. Use **Converter** para gerar o AZW3.
   3. Use **Enviar** para mandar ao Kindle.
6. Em mangá japonês, ligue **Leitura da direita para a esquerda**.

### O servidor de mangás

- Ele sobe sozinho quando o NovelShelf abre (se o suporte a mangás estiver ativado) e fecha quando o app fecha.
- Ele escuta só em `127.0.0.1`, numa porta livre escolhida a cada abertura. Não tem interface web nem ícone na bandeja.
- Se o NovelShelf travar ou for morto, o servidor também morre:
  - **Linux:** um vigia ligado ao app encerra o Java quando o app some.
  - **Windows:** um *Job Object* faz o mesmo.
- Ele usa cerca de 500 MB de RAM enquanto o app está aberto.
- O NovelShelf abre uma cópia só. Abrir de novo traz a janela que já está aberta para a frente, em vez de subir um segundo servidor.

### Como as páginas são ajustadas ao Kindle

- **Corte de margens:** bordas brancas ou pretas uniformes saem, e o conteúdo ganha zoom.
- **Páginas duplas** (mais largas que altas) viram duas, na ordem de leitura. Com a leitura da direita para a esquerda ligada, a metade direita vem primeiro.
- **Webtoon** (manhwa/manhua em tira longa): a tira é juntada e fatiada no tamanho exato da tela. O corte procura uma faixa lisa entre quadros para não partir balões, e fatias totalmente em branco são descartadas.
- **Ajuste à tela:** cada página fica com o tamanho exato do modelo escolhido, na página **Kindle** ou em **Ajustar corte**. As páginas saem em tons de cinza.
  - Kindle 11ª geração (6", 1072×1448).
  - Kindle Paperwhite 11ª/12ª geração (6,8", 1236×1648).
- **Marcado como quadrinho:** o AZW3 informa ao Kindle que é quadrinho, sem margem e em retrato, com a resolução da tela. Assim o Kindle mostra a página inteira, sem reservar espaço no topo.
- **Ajuste por obra:** na biblioteca, **Ajustar corte** abre um preview das telas exatas do Kindle. Lá dá para escolher:
  - formato (páginas ou tira contínua);
  - margens (sozinho, sem corte ou manual, para dar zoom);
  - páginas duplas (dividir, manter ou girar);
  - no webtoon, quanto repetir no topo da tela seguinte e até onde procurar um espaço entre quadros;
  - tons médios (gama).

  O ajuste fica salvo para aquela obra, e Converter e Enviar passam a usá-lo.
- **Formato:** o livro é um AZW3 de layout fixo (uma imagem por página, sumário por capítulo). Ele usa o mesmo ASIN e o mesmo nome de arquivo (`hq-<obra>.azw3`), então reenviar substitui o livro.

## Kindle: detalhes importantes

- **Formato.** O Kindle não lê EPUB copiado pelo cabo. O NovelShelf envia **AZW3**. O EPUB fica no PC como cópia universal.
- **Conexão.** Kindles antigos aparecem como **disco USB**. O Paperwhite de 11ª geração em diante usa **MTP**.
  - **Linux:** os dois funcionam. O MTP passa pelo `gvfs` (`gio`), o mesmo que o GNOME Arquivos usa. Se o Kindle não for encontrado, abra-o uma vez no gerenciador de arquivos e clique em **Procurar Kindle**.
  - **Windows:** hoje só disco USB. MTP no Windows está no [roadmap](#roadmap).
- **Substituição.** O arquivo no Kindle tem nome fixo por novel (`<fonte>-<slug>.azw3`, por exemplo `novelmania-overgeared.azw3`) e o mesmo ASIN em todas as versões, então reenviar troca o livro em vez de duplicar.
- **Capa.** Livros copiados pelo cabo não ganham capa da Amazon. O app grava a miniatura em `system/thumbnails/thumbnail_<ASIN>_EBOK_portrait.jpg`, o mesmo truque do Calibre. Se a pasta não estiver acessível (alguns modelos em MTP), o livro funciona normalmente, só que sem capa na grade.

## Arquitetura

Solução .NET 10 em C#, com interface em [Avalonia](https://avaloniaui.net) e [FluentAvalonia](https://github.com/amwx/FluentAvalonia) (Fluent Design).

```
src/
├── NovelShelf.Core                 contratos, biblioteca SQLite, download, exportação
├── NovelShelf.Sources.NovelMania   fonte do Novel Mania
├── NovelShelf.Sources.Illusia      fonte do Illusia
├── NovelShelf.Ebook                escritores de EPUB 3 e AZW3 (KF8), sem dependências
├── NovelShelf.Comics               ajuste das páginas ao Kindle (SkiaSharp) e AZW3 de layout fixo
├── NovelShelf.Suwayomi             instala, sobe e conversa com o servidor de extensões do Mihon
├── NovelShelf.Devices              detecção do Kindle e envio (disco e MTP)
└── NovelShelf.App                  interface (Avalonia + FluentAvalonia, MVVM)
tests/
├── NovelShelf.Tests                fontes, biblioteca, quadrinhos, Suwayomi, dispositivos
└── NovelShelf.Ebook.Tests          EPUB, AZW3 e páginas de quadrinho (inclui um leitor KF8 para validar a estrutura)
```

### Peças principais

| Peça | O que faz |
|---|---|
| `INovelSource` | Contrato de um site: buscar, ler a página da novel e ler um capítulo (texto + link do próximo). |
| `PoliteHttpClient` | HTTP com intervalo mínimo entre requisições e nova tentativa em `429`/`5xx`. |
| `ChapterHtmlSanitizer` | Limpa o HTML do capítulo e deixa só a formatação de texto (`p`, `em`, `strong`, títulos, listas), em XHTML válido. |
| `LibraryStore` | SQLite com novels, capítulos e o estado de envio ao Kindle. |
| `ChapterDownloader` | Baixa os próximos N capítulos a partir do último salvo. |
| `BookExporter` | Monta o EPUB e o AZW3 com ID estável. |
| `EpubWriter` | EPUB 3 com sumário (`nav` + `ncx`) e saída determinística. |
| `MobiBook` | Escritor AZW3/KF8: PalmDB, cabeçalhos MOBI/EXTH, esqueleto e fragmentos, NCX, FDST e capa. |
| `KindleLocator` / `KindleSender` | Acham o Kindle (disco ou MTP via `gio`) e copiam o livro, substituindo o anterior. |
| `IComicProvider` | Contrato de quadrinhos: fontes, explorar/buscar, detalhes, capítulos e páginas. |
| `SuwayomiInstaller` / `SuwayomiServer` | Baixam Java e Suwayomi com checksum fixo, sobem o servidor em `127.0.0.1` e o encerram junto com o app. |
| `SuwayomiClient` | Cliente GraphQL do Suwayomi: repositório do Project Nox, extensões e implementação do `IComicProvider`. |
| `ComicDownloader` | Baixa as páginas dos próximos capítulos para `comics/`. |
| `ComicPageProcessor` | Corta margens, divide páginas duplas, fatia webtoons e ajusta ao tamanho da tela. |
| `ComicBookBuilder` / `ComicExporter` | Montam o AZW3 de layout fixo com uma imagem por página. |

### Fonte Novel Mania

- Lê só páginas HTML públicas. O `robots.txt` do site proíbe `/api/`, e o app respeita isso.
- Dados da novel vêm do JSON-LD (`schema.org/Book`) da página.
- O texto do capítulo vem de `.rich-text-content`, e o próximo capítulo do `<link rel="next">`.
- Há um intervalo de 1,5 s entre requisições.

### Fonte Illusia

- O site é WordPress com o tema Fictioneer. O `robots.txt` libera navegadores comuns e bloqueia só robôs de IA; o app lê apenas as páginas HTML públicas.
- A busca usa a pesquisa do site filtrada por histórias (`/?s=<termo>&post_type=fcn_story`).
- A página da história traz a lista completa de capítulos; o primeiro da lista é o ponto de partida.
- O texto vem de `#chapter-content`, e o próximo capítulo do botão "próximo" da navegação. As notas de rodapé do tradutor viram uma lista no fim do capítulo.
- Capítulos protegidos por senha não são baixados; o download para com um aviso.
- As capas são WebP; o app converte para JPEG antes de montar o livro.

### Escritor AZW3

`NovelShelf.Ebook/Mobi` é um porte para C# da biblioteca Go [leotaku/mobi](https://github.com/leotaku/mobi) (MIT), com quatro ajustes para seguir o que o Calibre gera:

- o fragmento de texto é inserido **dentro** do `<body>` do esqueleto, gerando XHTML válido;
- a geometria do índice de esqueleto é gravada duas vezes, como o leitor KF8 espera;
- caracteres UTF-8 partidos entre registros de 4096 bytes recebem o "multibyte overlap";
- o título no esqueleto é escapado para XML.

Os testes remontam o arquivo gerado com um leitor KF8 escrito no próprio projeto. Eles conferem o texto, as páginas XHTML, o sumário, os metadados EXTH e a capa.

## Rodar a partir do código

Requisitos: [.NET SDK 10](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/uesleinasch/novelshelf.git
cd novelshelf
dotnet run --project src/NovelShelf.App
```

Gerar o AppImage localmente (precisa de `squashfs-tools`; o runtime do AppImage é baixado do projeto [type2-runtime](https://github.com/AppImage/type2-runtime), ou passe um com `APPIMAGE_RUNTIME=/caminho/runtime-x86_64`):

```bash
./scripts/build-appimage.sh   # gera artifacts/NovelShelf-vX.Y.Z-<commit>-x86_64.AppImage
```

Testes:

```bash
dotnet test                                 # unitários
NOVELSHELF_LIVE=1 dotnet test               # inclui teste contra o site real
NOVELSHELF_DUMP_DIR=/tmp/azw3 dotnet test   # grava AZW3 de exemplo para inspecionar

# servidor de mangás de verdade: instala na pasta (se preciso), baixa um capítulo
# do HQ Now! e do Akaito, converte e grava os AZW3 e amostras das páginas
NOVELSHELF_SUWAYOMI_ROOT=/tmp/suwayomi NOVELSHELF_DUMP_DIR=/tmp/azw3 dotnet test
```

## Uso responsável

- O app é para **leitura pessoal**. Não redistribua os livros gerados: as traduções pertencem às equipes dos sites.
- Apoie os tradutores. O Novel Mania aceita doações pelo [tipa.ai](https://tipa.ai/novelmania), e o Illusia tem página de apoio no próprio site.
- Nos quadrinhos, apoie as scans e as editoras oficiais. As extensões são da comunidade Project Nox. O NovelShelf não distribui extensões; você escolhe e instala as que quiser.
- O app respeita o `robots.txt` e espaça as requisições para não sobrecarregar o site. Não reduza esse intervalo.
- Este projeto não é afiliado ao Novel Mania, ao Illusia nem à Amazon. Kindle é marca da Amazon.

## Limitações conhecidas

- **Windows + Kindle em MTP** (Paperwhite 11ª geração em diante) ainda não é suportado. No Windows, só Kindles que aparecem como unidade de disco.
- O AZW3 ainda não foi testado em todos os modelos de Kindle. Se algo aparecer errado, abra uma issue com o modelo e a versão do firmware.
- O texto vai sem compressão PalmDoc. O arquivo fica maior, mas a leitura não muda.
- Imagens dentro dos capítulos de novel são descartadas. Só a capa é usada.
- Quadrinhos:
  - Cada obra vira **um** AZW3 com todos os capítulos baixados. Séries muito longas geram arquivos grandes, e a conversão refaz todas as páginas a cada envio.
  - Sites que exigem login ou que passam pelo Cloudflare podem não funcionar, porque o WebView do Suwayomi fica desligado.
  - O controle do servidor no Windows (Job Object) foi escrito, mas ainda não foi testado num Windows real.

## Roadmap

- [ ] MTP no Windows (WPD)
- [ ] Mais sites de novels
- [ ] Opção de dividir novels e quadrinhos muito longos em volumes
- [ ] Guardar as páginas já convertidas para acelerar reenvios
- [ ] Login e Cloudflare nas fontes de quadrinhos
- [ ] Compressão PalmDoc no AZW3
- [ ] Atualização automática ("baixar capítulos novos de todas as novels")

## Contribuir

Veja o [CONTRIBUTING.md](CONTRIBUTING.md), que traz o fluxo de branches (`main` → `production`), o padrão de commits, como lançar versão e como adicionar um site novo.

## Licença e créditos

- Código sob licença [MIT](LICENSE).
- Escritor AZW3 baseado em [leotaku/mobi](https://github.com/leotaku/mobi) (MIT). Detalhes em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Página de quadrinho no AZW3 inspirada no [kojirou](https://github.com/leotaku/kojirou) (MIT).
- O suporte a mangás baixa, na hora da ativação, o [Suwayomi-Server](https://github.com/Suwayomi/Suwayomi-Server) (MPL-2.0) e o [Eclipse Temurin](https://adoptium.net) 21 (GPLv2 com Classpath Exception). As extensões do [Project Nox](https://github.com/ProjectNox-org/manga-extensoes) (AGPL-3.0) rodam dentro do Suwayomi, em outro processo.
- Referências do formato: [MobileRead Wiki (MOBI/KF8)](https://wiki.mobileread.com/wiki/KF8) e o código do [Calibre](https://github.com/kovidgoyal/calibre).
