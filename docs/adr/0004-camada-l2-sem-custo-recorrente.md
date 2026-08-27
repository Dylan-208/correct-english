# ADR 0004 — Camada L2 sem custo recorrente

- **Data:** 2026-08-27
- **Status:** aceita
- **Substitui:** a parte da [ADR 0002](0002-motor-de-correcao.md) que definia a camada L2

## Contexto

A ADR 0002 definiu a camada L2 como Claude Opus 5 via Messages API, a ~R$ 0,12 por consulta
(~R$ 105/mês no volume estimado). Isso partia de uma premissa que não foi verificada: que o
custo poderia sair de uma assinatura já paga.

**Não pode.** A API da Anthropic é cobrada separadamente das assinaturas Claude Pro / Max.
Uma assinatura cobre o claude.ai e o Claude Code; ela não gera crédito de API para um
aplicativo próprio. Usar a API no app significa uma segunda conta, com cobrança própria.

O requisito, agora explícito: **o app não pode ter custo recorrente por fora do que já é pago.**

## Decisão

A configuração padrão do app tem **custo zero**. A camada L2 paga passa a ser opcional,
desligada por padrão.

### O que fica de graça, para sempre

| Camada | Motor | Custo | Cobre |
|---|---|---|---|
| **L0** | Hunspell `en_US` embarcado | zero, offline | ortografia — é o que desenha o sublinhado |
| **L1** | LanguageTool self-hosted em `localhost` | zero, offline | gramática, concordância, artigo, preposição, `their`/`there` |

O LanguageTool é LGPL-2.1, tem servidor HTTP embutido, roda offline depois de baixar os
dados, e — o detalhe que muda o projeto — **a API REST dele já devolve a explicação da
regra violada** junto com a sugestão. Não era só um detector.

### O "por quê em português", sem LLM

As mensagens do LanguageTool vêm em inglês, mas o conjunto de regras é **finito**. A
estratégia: traduzir a mensagem de cada regra na primeira vez que ela aparece, guardar em
cache local, e reusar para sempre. Depois de um mês de uso, o cache cobre os erros que
*você* comete de verdade, que são poucos e repetidos.

Custo: zero recorrente.

### O que se perde

Duas coisas, e vale ser honesto sobre elas:

1. **Reescrita natural.** O LanguageTool não sabe que `I have send the report to you` soa
   estrangeiro. Ele corrige para `I have sent the report to you` e para aí. A sugestão
   `I sent you the report` exige um modelo que entenda a frase.
2. **Tradução do texto selecionado.** Regra nenhuma traduz.

### Como recuperar essas duas, sem pagar

**LLM local via Ollama**, opcional. Avaliação honesta para o hardware desta máquina
(Core Ultra 5 125H, 16 GB de RAM, Arc integrada, sem GPU dedicada):

| Modelo | RAM | Latência estimada | Veredito |
|---|---|---|---|
| 3–4 B, Q4 | ~2,5 GB | 3–6 s | viável; qualidade razoável na tarefa estreita |
| 7–8 B, Q4 | ~5 GB | 8–15 s | lento demais para a janela do popup |

Atrito adicional: usar a Arc integrada exige builds com IPEX-LLM / OpenVINO, não o Ollama
padrão. Na CPU pura, ficaria no limite superior das estimativas acima.

**Decisão sobre o LLM local: adiada para a Fase 3**, quando o valor real das camadas L0 e
L1 já estiver medido pelo uso. Pode ser que a tradução não faça tanta falta quanto parece
hoje.

### Claude como provedor opcional

A camada L2 paga **continua no projeto**, desligada por padrão, ativável com chave própria.
Isso custa muito pouco para manter: a interface `ICorrectionProvider` já existe, então é uma
opção de configuração, não uma reescrita.

Cenário realista se algum dia for ligada: usada só em textos importantes (~5 por dia útil,
não 40), o custo cai para ~R$ 13/mês. A decisão fica com o usuário, com dado real na mão.

## Alternativas rejeitadas

- **DeepL API Free.** Era a candidata óbvia para tradução gratuita (500 mil caracteres/mês).
  **Não está mais disponível:** desde julho de 2026 a DeepL não vende mais API Free nem API
  Pro, e direciona novos clientes para os planos Developer e Growth — sendo que o Developer dá
  1 milhão de caracteres **no total**, não por mês. Descartada.
- **API paga como padrão.** Viola o requisito.
- **Serviços de tradução com camada gratuita em nuvem** (Azure, Google): não descartados, mas
  os limites atuais não foram confirmados, e todos criam dependência de conta, de rede e de
  uma política de preços que pode mudar — exatamente o problema que esta ADR resolve.
  Reavaliar só se o LLM local se provar inviável.

## Consequências

- O app passa a ser **integralmente offline** na configuração padrão. Isso melhora a
  privacidade de forma acidental mas real: sem L2 ligada, nenhum texto sai da máquina.
- A Fase 2 muda de conteúdo: em vez de "integrar Claude", passa a ser "integrar LanguageTool
  local + cache de tradução de regras". A Fase 1 **não muda em nada** — o motor falso e a
  interface `ICorrectionProvider` já isolavam esta decisão.
- A qualidade da explicação cai de "um modelo explicando em português natural" para
  "mensagem de regra traduzida". Aceitável: a mensagem do LanguageTool é escrita para
  humanos, não é código de erro.
