# EPG-AutoAdjusted (PT-BR)

Gerador próprio de EPG (padrão XMLTV) em **C# / .NET 10**, projetado para rodar **100% grátis via GitHub Actions** sem depender de outros repositórios Git.

## Como funciona

1. **Coleta Multi-Fonte em Cascata (Sem assinatura):**
   - **Fonte Primária (`ClaroTV+`):** Consulta diretamente a API pública de catálogo e grade da Claro TV+.
   - **Fonte Secundária / Fallback (`VivoPlay`):** Consulta a API pública da Vivo Play para preencher canais ausentes na Claro ou assumir automaticamente caso a API principal falhe.
2. **Auto-Ajuste de Nomes (`SD`, `HD`, `FHD`, `4K`, `H265`):**
   - Limpa sufixos de resolução do nome original da operadora e injeta múltiplas tags `<display-name>` dentro do mesmo `<channel>`.
   - Qualquer lista M3U de terceiros reconhece automaticamente todas as variações do canal sem precisar editar o arquivo `.m3u`.
3. **Trava de Segurança (Safety Gate):**
   - Só sobrescreve os arquivos `output/epg.xml` e `output/epg.xml.gz` se a coleta retornar quantidade válida de canais e programas.

## Comandos locais

```powershell
# Rodar suíte completa de testes unitários (xUnit)
dotnet test -c Release

# Rodar auto-teste rápido de normalização e aliases
dotnet run --project src/EpgGenerator -- --self-check

# Gerar EPG para 2 dias
dotnet run --project src/EpgGenerator -c Release -- --days 2 --output output --aliases aliases.json
```

## URL para usar no Player IPTV (após subir no GitHub)

Use preferencialmente o arquivo compactado `.gz` (carrega ~10x mais rápido no TiviMate / IPTV Smarters):

```text
https://raw.githubusercontent.com/CesarOvies/EPG-AutoAdjusted/main/output/epg.xml.gz
```
Ou descomprimido:
```text
https://raw.githubusercontent.com/CesarOvies/EPG-AutoAdjusted/main/output/epg.xml
```

## Aviso Legal (Disclaimer)

- Este projeto **não fornece, não hospeda, não transmite e não distribui** nenhum fluxo de vídeo, áudio, sinal de TV ou listas de reprodução (`.m3u` / `.m3u8`).
- O repositório atua exclusivamente como um agregador automatizado de metadados públicos de grade de programação (horários, títulos e sinopses) disponibilizados publicamente na web para fins informativos.
- Todas as marcas, nomes de canais e logotipos pertencem aos seus respectivos proprietários.

## Licença

Distribuído sob a licença [MIT](LICENSE).

