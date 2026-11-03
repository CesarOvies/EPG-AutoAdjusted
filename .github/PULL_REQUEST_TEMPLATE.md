## 📌 Resumo da Alteração

Descreva de forma objetiva o que este Pull Request adiciona ou corrige:
- [ ] Adição de novos apelidos de canais em `aliases.json`
- [ ] Novo provedor de EPG (`IEpgProvider`) ou melhoria nos coletores existentes
- [ ] Melhoria de documentação / CI

---

## ✅ Checklist de Verificação Automática

- [ ] Executei `dotnet test -c Release` e todos os testes unitários passaram
- [ ] Executei `dotnet run --project src/EpgGenerator -c Release -- --self-check` sem erros
- [ ] Mantive a regra de **zero pacotes NuGet externos** em `src/EpgGenerator`
