# Correct English

Corretor de inglês que roda no Windows, para quem escreve inglês todo dia sem ser nativo.

Seleciona um texto em qualquer aplicativo, aperta `Ctrl+C` três vezes, e aparece uma janelinha com:

- a **tradução para português**, para conferir se o que você escreveu diz o que você quis dizer;
- a **versão corrigida**, com a explicação de cada erro em português;
- um botão **Replace** que troca o texto no lugar de onde você o selecionou.

Enquanto você digita, palavra errada em inglês recebe um aviso em tempo real — do mesmo jeito que o Windows já faz em português.

> **Status:** planejamento concluído, implementação não iniciada. Veja [as fases](#fases).

---

## Por que existe

Corretor de português nativo do Windows funciona em todo lugar. Corretor de inglês, não — ou é pago, ou é extensão de navegador só, ou não explica *por que* você errou. Este projeto tenta resolver as três coisas de uma vez, com o código aberto para ser auditável (ver [Privacidade](#privacidade), que não é um detalhe aqui).

## Como a correção funciona

Três camadas empilhadas. Nenhuma resolve sozinha, e a divisão é o que faz o app não ficar lento nem caro.

| Camada | Motor | Latência | Custo | O que pega |
|---|---|---|---|---|
| **L0** Ortografia | Hunspell `en_US` + SymSpell, embarcado | < 1 ms | **zero**, offline | `informations`, `recieve`, `alot` |
| **L1** Gramática | LanguageTool em contêiner Docker | 40–150 ms | **zero**, offline | concordância, artigo, preposição, pontuação |
| **PT** Tradução | LibreTranslate self-hosted em Docker | ~300 ms | **zero**, offline | tradução do texto corrigido para português |
| **L2** Reescrita natural | ainda ausente — decisão aberta | — | — | saber que uma frase correta soa estrangeira |

**A configuração padrão do app não tem custo recorrente e funciona 100% offline.** L0 e L1 rodam no seu PC, de graça e sem limite, e são elas que alimentam o aviso em tempo real.

A API REST do LanguageTool já devolve **a explicação da regra violada**, não só a correção. Essas mensagens vêm em inglês, mas o conjunto de regras é finito: o app traduz cada mensagem na primeira vez que ela aparece e guarda em cache. Depois de um mês, o cache cobre os erros que *você* comete de verdade — que são poucos e repetidos.

O que as regras **não** fazem: saber que `I have sent the report to you` está correto mas soa estrangeiro, e que um nativo escreveria `I sent you the report`. Isso exige um modelo que entenda a frase, e é o papel da camada L2 opcional — LLM local via Ollama, ou a API do Claude com chave própria. Ver [ADR 0004](docs/adr/0004-camada-l2-sem-custo-recorrente.md).

## Atalhos

| Ação | Atalho |
|---|---|
| Abrir com o texto selecionado | `Ctrl+C` `Ctrl+C` `Ctrl+C` |
| Aplicar a correção | `Enter` |
| Fechar sem mexer no texto | `Esc` |
| Trocar o tom (neutro / formal / informal) | `Tab` |
| Só traduzir, sem corrigir | `Ctrl+Shift+T` |

O `Ctrl+C` triplo resolve de graça o problema mais chato: ler o texto selecionado de **qualquer** aplicativo. O texto já está no clipboard. Sem isso, seria preciso interrogar o app pelo UI Automation do Windows, que falha em Electron, terminal e editores em canvas.

## Privacidade

Este app usa um hook global de teclado. Tecnicamente, é a mesma API de um keylogger. Isso cria obrigações que não são opcionais:

- **Lista de permissão, não de bloqueio.** Por padrão, nenhum aplicativo é observado. Você liga um por um.
- **Nada em disco.** O buffer guarda apenas a frase que está sendo digitada, só em RAM.
- **Zero teclas na internet.** A API do Claude é chamada apenas com o texto que *você* selecionou, sob comando explícito. O que você digita nunca sai da máquina.
- **Campos de senha são ignorados**, detectados por `ES_PASSWORD` e pela propriedade `IsPassword` do UI Automation. Não é configurável.
- **Chave de API no Windows Credential Manager**, nunca em arquivo de configuração.

O código é aberto justamente para que essas afirmações sejam verificáveis, e não uma promessa.

## Fases

| | Fase | Estado |
|---|---|---|
| 0 | Repositório, licença e decisões registradas em ADR | ✅ concluída |
| 1 | Esqueleto: hook do atalho, popup, e `Replace` funcionando com correção falsa | ✅ concluída |
| 2a | Camada L0: Hunspell embarcado — **a partir daqui o app é útil de verdade** | ✅ concluída |
| 2b | Camada L1: LanguageTool (gramática) + tradução para português | ✅ concluída |
| 3 | Aviso em tempo real perto do cursor + lista de permissão por app | ⬜ próxima |
| 4 | Distribuição: instalador, auto-update, iniciar com o Windows | ⬜ |
| 5 | Sublinhado ondulado nativo (overlay + UI Automation) — **só se ainda fizer falta** | ⬜ a decidir |

A Fase 1 valida de propósito a parte que mais quebra — guardar a janela anterior, devolver o foco, colar e restaurar o clipboard — usando uma correção fixa e falsa, antes de qualquer motor de correção entrar na história.

### Resultado da Fase 1 — 27/08/2026

Testada manualmente com o motor falso (texto vira MAIÚSCULAS) em **Bloco de Notas, Google Chrome, Slack, WhatsApp, Word e VS Code**. O `Replace` funcionou em todos, o `Ctrl+C` normal continuou intacto, e o clipboard foi restaurado corretamente após a troca.

Isso importa mais do que parece: significa que a mecânica funciona tanto em Win32 clássico quanto em Electron e no Office — as três famílias em que esse tipo de app costuma falhar. O caminho A do [ADR 0003](docs/adr/0003-caminho-do-sublinhado.md) está confirmado na prática, não só no papel.

Um bug real apareceu no caminho: o fechamento da janela era reentrante por construção — fechar provocava a desativação, e a desativação era o gatilho de fechar. Corrigido na estrutura, não só no sintoma (commit `378957e`).

## Decisões

As três decisões estruturais estão registradas em [`docs/adr/`](docs/adr/):

- [0001 — Stack: C# / .NET 8 + WPF](docs/adr/0001-stack.md)
- [0002 — Motor de correção em três camadas](docs/adr/0002-motor-de-correcao.md)
- [0003 — Caminho do aviso em tempo real](docs/adr/0003-caminho-do-sublinhado.md)
- [0004 — Camada L2 sem custo recorrente](docs/adr/0004-camada-l2-sem-custo-recorrente.md)
- [0005 — Tradução com LibreTranslate self-hosted](docs/adr/0005-traducao-com-libretranslate.md)
- [0006 — Gramática com LanguageTool, e a fronteira entre L0 e L1](docs/adr/0006-gramatica-com-languagetool.md)

O plano visual completo, com mockups das telas, está em [`docs/plano.html`](docs/plano.html) — abra no navegador.

## Rodando localmente

```powershell
# 1. Baixa o dicionário Hunspell en_US (2 arquivos, ~1 MB).
#    Não é versionado: é dado de terceiro, com licença própria.
.\scripts\get-dictionaries.ps1

# 2. Sobe o servidor de tradução (opcional). Primeira vez baixa a imagem
#    e os modelos en<->pt; acompanhe com "docker compose logs -f".
docker compose up -d

# 3. Compila e testa
dotnet build CorrectEnglish.sln
dotnet test tests\CorrectEnglish.Core.Tests

# 4. Roda (fica na bandeja do sistema)
dotnet run --project src\CorrectEnglish.App
```

**As duas dependências externas são opcionais, e cada ausência degrada só a si mesma:**

- **Sem o dicionário**, a janela explica que a ortografia está indisponível e diz qual script rodar. Não há caso especial espalhado pelo código — quem cuida disso é um `UnavailableCorrectionProvider` que implementa a mesma interface dos outros motores.
- **Sem o contêiner de tradução**, a correção continua funcionando e a seção de português simplesmente não aparece. A disponibilidade é verificada a cada chamada, não na inicialização: subir o Docker com o app já aberto faz a tradução começar a aparecer na hora, sem reiniciar nada.

## Requisitos de desenvolvimento

- Windows 10 1809+ ou Windows 11
- .NET 8 SDK
- Docker — opcional, para a gramática (L1) e a tradução (PT). **Sem Java:** o LanguageTool roda em contêiner.
- *Nada mais.* Sem chave de API, sem conta em serviço nenhum, sem custo.

## Licença

MIT. Veja [LICENSE](LICENSE).
