# ADR 0002 — Motor de correção em três camadas

- **Data:** 2026-08-27
- **Status:** aceita

## Contexto

O app precisa fazer três coisas que parecem uma só, mas têm requisitos opostos:

1. avisar de palavra errada **enquanto se digita** — precisa ser instantâneo e rodar milhares de vezes por dia;
2. corrigir gramática;
3. traduzir, explicar o erro em português e reescrever de forma natural — precisa *entender* o texto.

Usar um LLM para as três dá um app lento e caro. Usar só regras dá um app que não entende o que
você quis dizer. Nenhum motor único atende os três requisitos.

## Decisão

Três camadas independentes, acionadas por gatilhos diferentes.

| Camada | Motor | Latência | Custo | Gatilho |
|---|---|---|---|---|
| **L0** Ortografia | Hunspell `en_US` (SCOWL) + SymSpell, embarcado | < 1 ms | grátis, offline | a cada palavra digitada |
| **L1** Gramática | LanguageTool self-hosted em `localhost`, opcional | 40–150 ms | grátis, offline | debounce de 800 ms |
| **L2** Significado | Claude Opus 5, Messages API | 1–3 s | ~R$ 0,12/consulta | **só** sob comando do usuário |

**Regra inviolável: L2 nunca é chamada em segundo plano.** Só quando o usuário aperta o atalho.
Isso resolve custo, latência e privacidade de uma vez.

### Detalhes da camada L2

- **Saída estruturada** via `output_config.format`, com schema fixo
  (`traducao_pt`, `texto_corrigido`, `tom`, `correcoes[]`, `alternativas[]`, `confianca`).
  O app nunca recebe uma resposta que não caiba na tela.
- **Streaming**, para a tradução aparecer antes da correção terminar de ser gerada. Reduz a espera
  percebida de ~2 s para ~400 ms.
- **`output_config.effort: "low"`.** No Opus 5 o raciocínio adaptativo é ligado por padrão, o que é
  desperdício para uma frase de e-mail. Preferido a desligar o thinking, que no Opus 5 tem efeitos
  colaterais conhecidos (chamada de ferramenta vazando para o texto visível, tags internas no output).
- **Prompt caching** no prompt de sistema. O prefixo mínimo cacheável é ~1024 tokens, então vale
  escrever um prompt de sistema generoso, com exemplos de erros típicos de falante de português.
- **Cache local por hash do texto**, para não pagar duas vezes pela mesma frase.
- Modelo configurável: Sonnet 5 e Haiku 4.5 como alternativas se o volume crescer.

## Consequências

- O aviso em tempo real **nunca** depende de rede nem gasta dinheiro — vem de L0 e L1.
- O app é utilizável offline com funcionalidade reduzida (sem tradução e sem explicação).
- L1 custa um servidor Java parado consumindo ~1 GB de RAM. Por isso é **opcional**, desligado por
  padrão, e o app funciona sem ele.
- Três motores para manter e três formatos de resultado para unificar numa única lista de sugestões.
