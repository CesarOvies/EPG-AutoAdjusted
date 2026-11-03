# Guia de Contribuição & Fork — EPG-AutoAdjusted

Obrigado pelo interesse em contribuir ou criar seu próprio fork do **EPG-AutoAdjusted**!

---

## 🍴 Como fazer um Fork (Para ter seu próprio gerador independente)

Você pode rodar este projeto na sua própria conta do GitHub **100% grátis** sem tocar no código:

1. Clique no botão **Fork** no topo direito deste repositório.
2. No seu novo repositório criado, acesse a aba **Actions** e clique no botão verde **"I understand my workflows, go ahead and enable them"**.
3. Acesse **Settings -> Actions -> General -> Workflow permissions** e marque **"Read and write permissions"**, clicando em **Save**.
4. Pronto! O seu GitHub Actions vai atualizar seu próprio arquivo a cada 12h (ou quando você clicar em **Run workflow**) e o painel `output/stats.svg` usará automaticamente os dados do seu repositório.

Sua URL própria ficará disponível em:
```text
https://raw.githubusercontent.com/<SEU_USUARIO>/EPG-AutoAdjusted/main/output/epg.xml.gz
```

---

## 🤝 Como Contribuir com o Projeto Principal

### Caso 1: Adicionar apelido de um canal que não pareou na sua lista M3U (Sem programar em C#)

99% dos ajustes de pareamento de canais são feitos editando apenas o arquivo [`aliases.json`](aliases.json):

1. Abra o arquivo [`aliases.json`](aliases.json).
2. Localize o nome base do canal na seção `"customAliases"` (ou adicione uma nova chave correspondente ao nome oficial da Claro TV+ / Vivo Play).
3. Adicione o nome como ele aparece na sua lista M3U (sem precisar colocar `SD`, `HD`, `FHD`, `4K` ou `H265`, pois o gerador já aplica todos os sufixos automaticamente):
   ```json
   "TELECINE ACTION": [
     "TC ACTION",
     "telecine.action.br",
     "SEU_NOVO_APELIDO_AQUI"
   ]
   ```
4. Abra um **Pull Request**! O nosso workflow automático (`CI — Build & Tests`) vai validar se o JSON está íntegro e se todos os testes passam.

---

### Caso 2: Adicionar uma nova Fonte/Operadora de EPG (C# / .NET 10)

Se quiser adicionar uma nova fonte pública de programação no sistema de cascata:

1. Crie uma classe em `src/EpgGenerator/Providers/` implementando a interface [`IEpgProvider`](src/EpgGenerator/Providers/IEpgProvider.cs).
2. **Regra de ouro (Resiliência):** Seu provedor nunca deve lançar exceção não tratada em caso de erro de rede/HTTP — retorne lista vazia ou parcial para que o próximo provedor da cascata assuma.
3. **Zero pacotes NuGet:** Use apenas bibliotecas nativas do .NET 10 (`System.Net.Http`, `System.Text.Json`, `System.Xml`).
4. Registre seu provedor no array `providers` dentro de [`Program.cs`](src/EpgGenerator/Program.cs).
5. Adicione um teste unitário com `FakeHttpMessageHandler` em [`tests/EpgGenerator.Tests/EpgGeneratorTests.cs`](tests/EpgGenerator.Tests/EpgGeneratorTests.cs).

### Rodando os testes localmente

```powershell
# 1. Executar suíte de testes unitários (xUnit)
dotnet test -c Release

# 2. Executar auto-verificação rápida
dotnet run --project src/EpgGenerator -c Release -- --self-check
```
