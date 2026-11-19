# NovelShelf

Leia web novels no seu **Kindle**, sem jailbreak, sem servidor e sem Calibre.

O NovelShelf é um app desktop (Linux e Windows) que busca novels na web, baixa os capítulos para uma biblioteca no seu PC, monta **um livro por novel** e envia o livro para o Kindle pelo cabo USB. Quando saem capítulos novos, você baixa e reenvia: o livro no Kindle é **substituído**, sem criar cópias, e a leitura continua de onde parou.

> Site suportado hoje: [Novel Mania](https://novelmania.com.br). A arquitetura já está pronta para receber outros sites.

---

## Sumário

- [Por que existe](#por-que-existe)
- [Como funciona](#como-funciona)
- [Instalação](#instalação)
- [Uso](#uso)
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
| Linux x64 | `NovelShelf-vX.Y.Z-linux-x64.tar.gz` | extraia e rode `./NovelShelf` |
| Windows x64 | `NovelShelf-vX.Y.Z-win-x64.zip` | extraia e rode `NovelShelf.exe` |

Os executáveis já trazem o runtime do .NET. Não é preciso instalar nada.

## Uso

1. **Buscar novels.** Digite o nome ou cole o link da novel (ou de um capítulo) e clique na novel para ver sinopse, gêneros e total de capítulos.
2. **Adicionar à biblioteca.** A novel vai para a aba **Biblioteca**.
3. **Baixar capítulos.** Escolha quantos capítulos baixar (padrão: 50) e clique em **Baixar**. Dá para cancelar; o que já baixou fica salvo.
4. **Enviar para o Kindle.** Conecte o Kindle no USB, desbloqueie a tela e clique em **Enviar**. O app gera o livro e copia para o aparelho.
5. **Ejete o Kindle.** Só depois de ejetar ele atualiza a biblioteca.

A lista mostra, para cada novel, quantos capítulos estão no PC e quantos já estão no Kindle. Por exemplo: *"Kindle tem 120 capítulos — 15 novos para enviar"*.

O botão **Gerar livro** cria o EPUB e o AZW3 sem enviar, para usar em outros leitores. **Abrir pasta** mostra onde os arquivos ficam.

### Onde ficam os dados

| Sistema | Pasta |
|---|---|
| Linux | `~/.local/share/NovelShelf/` |
| Windows | `%LOCALAPPDATA%\NovelShelf\` |

Dentro dela: `library.db` (biblioteca), `books/` (EPUB e AZW3 gerados) e `covers/` (capas).

## Kindle: detalhes importantes

- **Formato.** O Kindle não lê EPUB copiado pelo cabo. O NovelShelf envia **AZW3**. O EPUB fica no PC como cópia universal.
- **Conexão.** Kindles antigos aparecem como **disco USB**. O Paperwhite de 11ª geração em diante usa **MTP**.
  - **Linux:** os dois funcionam. O MTP passa pelo `gvfs` (`gio`), o mesmo que o GNOME Arquivos usa. Se o Kindle não for encontrado, abra-o uma vez no gerenciador de arquivos e clique em **Procurar Kindle**.
  - **Windows:** hoje só disco USB. MTP no Windows está no [roadmap](#roadmap).
- **Substituição.** O arquivo no Kindle tem nome fixo por novel (`novelmania-<slug>.azw3`) e o mesmo ASIN em todas as versões, então reenviar troca o livro em vez de duplicar.
- **Capa.** Livros copiados pelo cabo não ganham capa da Amazon. O app grava a miniatura em `system/thumbnails/thumbnail_<ASIN>_EBOK_portrait.jpg`, o mesmo truque do Calibre. Se a pasta não estiver acessível (alguns modelos em MTP), o livro funciona normalmente, só que sem capa na grade.

## Arquitetura

Solução .NET 10 em C#, com interface em [Avalonia](https://avaloniaui.net) e [FluentAvalonia](https://github.com/amwx/FluentAvalonia) (Fluent Design).

```
src/
├── NovelShelf.Core                 contratos, biblioteca SQLite, download, exportação
├── NovelShelf.Sources.NovelMania   fonte do Novel Mania
├── NovelShelf.Ebook                escritores de EPUB 3 e AZW3 (KF8), sem dependências
├── NovelShelf.Devices              detecção do Kindle e envio (disco e MTP)
└── NovelShelf.App                  interface (Avalonia + FluentAvalonia, MVVM)
tests/
├── NovelShelf.Tests                fontes, biblioteca, download, dispositivos
└── NovelShelf.Ebook.Tests          EPUB e AZW3 (inclui um leitor KF8 para validar a estrutura)
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

### Fonte Novel Mania

- Lê só páginas HTML públicas. O `robots.txt` do site proíbe `/api/`, e o app respeita isso.
- Dados da novel vêm do JSON-LD (`schema.org/Book`) da página.
- O texto do capítulo vem de `.rich-text-content`, e o próximo capítulo do `<link rel="next">`.
- Há um intervalo de 1,5 s entre requisições.

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

Testes:

```bash
dotnet test                                 # unitários
NOVELSHELF_LIVE=1 dotnet test               # inclui teste contra o site real
NOVELSHELF_DUMP_DIR=/tmp/azw3 dotnet test   # grava AZW3 de exemplo para inspecionar
```

## Uso responsável

- O app é para **leitura pessoal**. Não redistribua os livros gerados: as traduções pertencem às equipes dos sites.
- Apoie os tradutores. O Novel Mania aceita doações pelo [tipa.ai](https://tipa.ai/novelmania).
- O app respeita o `robots.txt` e espaça as requisições para não sobrecarregar o site. Não reduza esse intervalo.
- Este projeto não é afiliado ao Novel Mania nem à Amazon. Kindle é marca da Amazon.

## Limitações conhecidas

- **Windows + Kindle em MTP** (Paperwhite 11ª geração em diante) ainda não é suportado. No Windows, só Kindles que aparecem como unidade de disco.
- O AZW3 ainda não foi testado em todos os modelos de Kindle. Se algo aparecer errado, abra uma issue com o modelo e a versão do firmware.
- O texto vai sem compressão PalmDoc. O arquivo fica maior, mas a leitura não muda.
- Imagens dentro dos capítulos são descartadas. Só a capa é usada.

## Roadmap

- [ ] MTP no Windows (WPD)
- [ ] Mais sites de novels
- [ ] Opção de dividir novels muito longas em volumes
- [ ] Compressão PalmDoc no AZW3
- [ ] Atualização automática ("baixar capítulos novos de todas as novels")

## Contribuir

Veja o [CONTRIBUTING.md](CONTRIBUTING.md), que traz o fluxo de branches (`main` → `production`), o padrão de commits, como lançar versão e como adicionar um site novo.

## Licença e créditos

- Código sob licença [MIT](LICENSE).
- Escritor AZW3 baseado em [leotaku/mobi](https://github.com/leotaku/mobi) (MIT). Detalhes em [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- Referências do formato: [MobileRead Wiki (MOBI/KF8)](https://wiki.mobileread.com/wiki/KF8) e o código do [Calibre](https://github.com/kovidgoyal/calibre).
