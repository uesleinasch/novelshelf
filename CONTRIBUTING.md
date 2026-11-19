# Fluxo de trabalho

## Branches

| Branch | Papel |
|---|---|
| `main` | Integração. Recebe PR das branches de trabalho. |
| `production` | Versões lançadas. Só recebe PR vindo da `main`. Cada merge vira uma tag `vX.Y.Z`. |
| `feat/*`, `fix/*`, `chore/*`, `docs/*` | Trabalho. Saem da `main` e voltam para ela via PR. |

As duas branches protegidas só aceitam PR com o check `ci` verde (build com `-warnaserror` + testes). Push direto e force push são bloqueados, inclusive para admin.

## Commits

[Conventional Commits](https://www.conventionalcommits.org/), um commit por mudança, com escopo do projeto tocado:

```
feat(novelmania): ...
fix(ebook): ...
feat(app): ...
chore(release): v0.2.0
```

Escopos usados: `core`, `novelmania`, `ebook`, `devices`, `app`, `ci`, `release`.

## Dia a dia

```bash
git switch main && git pull
git switch -c feat/minha-coisa
# commits pequenos, um por mudança
git push -u origin feat/minha-coisa
gh pr create --base main --fill
gh pr merge --merge --delete-branch
```

## Lançar versão

```bash
# 1. subir a versão na main
git switch main && git pull
git switch -c chore/release-0.2.0
sed -i 's#<Version>.*</Version>#<Version>0.2.0</Version>#' Directory.Build.props
git commit -am "chore(release): v0.2.0"
git push -u origin chore/release-0.2.0
gh pr create --base main --fill && gh pr merge --merge --delete-branch

# 2. promover main -> production
gh pr create --base production --head main --title "release: v0.2.0" --body "Release v0.2.0"
gh pr merge --merge

# 3. tag no topo da production
git fetch origin
git tag v0.2.0 origin/production
git push origin v0.2.0
```

A tag dispara `.github/workflows/release.yml`. Ele confere se a tag está na `production` e se bate com o `<Version>` do `Directory.Build.props`. Depois roda os testes, publica os executáveis Linux (`tar.gz`) e Windows (`zip`) e cria o GitHub Release.

Acompanhar: `gh run watch` e `gh release view v0.2.0`.

## Adicionar um site novo

1. Crie `src/NovelShelf.Sources.<Site>` implementando `INovelSource` (`NovelShelf.Core.Sources`).
2. Leia só páginas permitidas pelo `robots.txt` do site e use o `PoliteHttpClient` (intervalo entre requisições).
3. Devolva o capítulo já limpo com `ChapterHtmlSanitizer.ToXhtml`.
4. Registre a fonte em `src/NovelShelf.App/Services/AppServices.cs`.
5. Testes de parsing com HTML sintético em `tests/NovelShelf.Tests/Sources` e, se quiser, um teste ao vivo protegido por `NOVELSHELF_LIVE=1`.
