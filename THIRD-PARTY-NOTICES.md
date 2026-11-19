# Third-party notices

## leotaku/mobi

`src/NovelShelf.Ebook/Mobi` is a C# port of https://github.com/leotaku/mobi (commit bf3c8d0), with changes to match Calibre's KF8 output (fragment insert position inside `<body>`, repeated skeleton geometry, multibyte overlap in text records, escaped skeleton title).

```
MIT License

Copyright (c) 2020-2026 Leo Gaskin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

```

---

## kojirou

O formato das páginas de quadrinho no AZW3 (um bloco de texto escondido seguido de uma imagem por página, com layout fixo) segue o que o [kojirou](https://github.com/leotaku/kojirou) gera. Nenhum código foi copiado. O kojirou é distribuído sob a licença MIT, Copyright (c) Leo Gaskin.

## Componentes baixados em tempo de execução

Estes programas **não** fazem parte do NovelShelf nem dos pacotes de release. Eles são baixados dos sites oficiais quando o usuário ativa o suporte a mangás, e o SHA-256 de cada arquivo é conferido.

| Componente | Licença | Origem |
|---|---|---|
| Suwayomi-Server | MPL-2.0 | https://github.com/Suwayomi/Suwayomi-Server |
| Eclipse Temurin JRE 21 | GPLv2 com Classpath Exception | https://adoptium.net |
| Extensões do Project Nox (instaladas pelo usuário) | AGPL-3.0 | https://github.com/ProjectNox-org/manga-extensoes |

As extensões rodam dentro do Suwayomi, num processo separado. O NovelShelf conversa com ele por HTTP em `127.0.0.1` e não incorpora o código delas.
