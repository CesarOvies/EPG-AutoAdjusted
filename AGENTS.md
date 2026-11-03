# Agent Instructions — EPG-AutoAdjusted

Guia rápido para agentes de IA (Antigravity, Copilot, Cursor, Claude Code) que operam neste repositório.

## Objetivo do Projeto

Gerador autônomo de EPG (padrão XMLTV) para canais brasileiros (abertos e fechados) em **C# / .NET 10**, sem dependências externas no projeto principal (`src/EpgGenerator`), executado via **GitHub Actions** (`output/epg.xml` e `output/epg.xml.gz`).

## Arquitetura e Fluxo de Dados

1. **Coletores (`src/EpgGenerator/Providers/IEpgProvider.cs`):**
   - `ClaroTvProvider.cs`: Fonte primária (`clarotvmais.com.br`). Busca catálogo de canais e grade em lotes (`BatchSize = 40`). Ignora grades com `<= 1` programa (placeholder de grade indisponível da Claro).
   - `VivoPlayProvider.cs`: Fonte secundária/fallback (`contentapi-br.cdn.telefonica.com`). Busca catálogo ativo (`LCH` case-sensitive) e grade em janelas de 24h (`BatchSize = 20`, `limit=1000`).
2. **Agregação e Cascata (`src/EpgGenerator/EpgAggregator.cs`):**
   - Unifica canais entre operadoras usando `ChannelNormalizer.ToCanonicalId(name)` (ex: `"AMC HD"` e `"AMC"` -> `"amc.br"`).
   - **Regra de prioridade:** O primeiro provedor com grade não-vazia vence. Provedores subsequentes preenchem apenas canais sem grade ou canais inexistentes no provedor anterior.
   - **Safety Gate (`PassesSafetyGate`):** Nunca sobrescreve `output/epg.xml` se o total coletado for menor que `DefaultMinChannels` (10) ou `DefaultMinProgrammes` (100).
3. **Normalização e Auto-Ajuste M3U (`src/EpgGenerator/ChannelNormalizer.cs` + `aliases.json`):**
   - Remove sufixos de resolução/codec (`SD`, `HD`, `FHD`, `4K`, `UHD`, `H265`, `HEVC`) para extrair o nome base.
   - Expande automaticamente dezenas de tags `<display-name>` (`Canal`, `Canal HD`, `Canal [HD]`, `Canal (HD)`, `Canal FHD H265`, mais `customAliases` de `aliases.json`) dentro do mesmo `<channel>` para compatibilidade imediata com listas M3U de terceiros.
4. **Escrita Atômica (`src/EpgGenerator/XmlTvWriter.cs`):**
   - Gera `.xml.tmp` e `.xml.gz.tmp` com fuso horário fixo de Brasília (`-0300`) e faz `File.Move(..., overwrite: true)` apenas ao finalizar com sucesso.

## Regras para Modificações (Invariantes)

- **Zero pacotes NuGet em `src/EpgGenerator`:** Use apenas bibliotecas nativas do .NET (`System.Net.Http`, `System.Text.Json`, `System.Xml`).
- **Resiliência de Rede:** Falha em um lote ou provedor **nunca** deve lançar exceção não tratada; o provedor deve retornar lista vazia ou parcial para permitir que o próximo provedor da cascata assuma.
- **Para adicionar novo canal/apelido:** Edite apenas `aliases.json` (não adicione hardcodes em `ChannelNormalizer.cs`).
- **Para adicionar nova fonte de EPG:** Implemente `IEpgProvider` em `src/EpgGenerator/Providers/`, registre no array `providers` em `Program.cs` na ordem de prioridade desejada e adicione teste unitário com `FakeHttpMessageHandler` em `tests/EpgGenerator.Tests/EpgGeneratorTests.cs`.

## Comandos Obrigatórios de Verificação

Sempre rode antes de finalizar qualquer alteração:

```powershell
# 1. Rodar suíte de testes unitários (xUnit)
dotnet test -c Release

# 2. Rodar auto-verificação rápida do binário
dotnet run --project src/EpgGenerator -c Release -- --self-check
```

