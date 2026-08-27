# ADR 0005 — Tradução com LibreTranslate self-hosted

- **Data:** 2026-08-27
- **Status:** aceita
- **Contexto anterior:** [ADR 0004](0004-camada-l2-sem-custo-recorrente.md)

## Contexto

A ADR 0004 tirou a camada paga da configuração padrão e listou duas capacidades perdidas:
tradução e reescrita natural. A decisão sobre como recuperá-las foi adiada para a Fase 3,
com a aposta de que um mês de uso mostraria se faziam falta.

A aposta estava errada quanto ao prazo. No primeiro teste da camada de ortografia, a
ausência da tradução foi imediatamente notada — ela era o **primeiro** item do pedido
original, não um extra.

Ambiente disponível: Docker 29.6.2 com daemon ativo; Python 3.14.6 (versão nova demais
para ter wheels confiáveis de `ctranslate2` e `argostranslate`, o que descarta instalar
LibreTranslate direto por `pip`).

## Decisão

**LibreTranslate self-hosted em contêiner Docker**, escutando em `localhost:5000`,
carregando apenas o par `en,pt`.

Três decisões de projeto que acompanham:

### 1. Traduz o texto corrigido, não o original

Contraintuitivo, então vale o registro: erro de digitação degrada tradução automática de
forma desproporcional — `reprot` não é uma palavra, e o modelo chuta. Traduzir depois da
correção significa traduzir uma frase bem formada, com a mesma intenção.

O risco assumido: se a correção estiver errada, a tradução propaga o erro. Aceitável,
porque o texto corrigido está visível na tela ao lado da tradução — o usuário vê os dois.

### 2. É um decorador, não uma camada nova

`TranslatingCorrectionProvider` envolve qualquer `ICorrectionProvider` e acrescenta o campo
de tradução. Não sabe nada sobre ortografia. Quando a camada L1 (LanguageTool) entrar, ela
ganha tradução de graça, sem uma linha a mais.

### 3. Degradação silenciosa é obrigatória

Se o contêiner não estiver rodando, a tradução falha e **a correção continua funcionando**.
O decorador captura qualquer exceção do tradutor e devolve o resultado da correção intacto;
a janela então oculta a seção de português. Corrigir é a função principal, traduzir é
acréscimo — e acréscimo não tem direito de derrubar o principal.

Consequência boa: se o usuário subir o contêiner com o app já aberto, a tradução passa a
aparecer sozinha, sem reiniciar nada. A verificação de disponibilidade acontece a cada
chamada, não uma vez na inicialização.

## Alternativas rejeitadas

- **LLM local via Ollama.** Recuperaria as duas capacidades perdidas, não só a tradução, e
  com qualidade melhor. Rejeitada pelo custo em latência (3–6 s contra ~300 ms) e em
  recursos (~3 GB de RAM em uma máquina de 16 GB). A reescrita natural fica pendente.
- **LibreTranslate via `pip`.** O Python instalado é 3.14.6, novo demais para as
  dependências nativas do projeto.
- **API paga.** Viola o requisito da ADR 0004.

## Consequências

- Nova dependência de runtime: Docker. É opcional — o app funciona sem, apenas sem traduzir.
- A qualidade em PT-BR é de modelo Argos/OpenNMT, claramente abaixo de um LLM. Suficiente
  para conferir sentido de frase curta, insuficiente para texto que será publicado.

### Medido na primeira execução (27/08/2026)

Latência: **309 ms** por frase, batendo com a estimativa.

Qualidade: **o modelo produz português europeu, não brasileiro.** Entrada
`I sent you the report yesterday. Could you confirm whether everything looks correct with
the numbers?` devolveu:

> Enviei-lhe o relatório ontem. Pode confirmar se tudo parece **correcto** com os números?

Dois desvios de PT-BR numa frase: `correcto` (grafia pré-acordo, europeia) e `Enviei-lhe`
(mesóclise/colocação que brasileiro não usa — seria "Te enviei").

O LibreTranslate não oferece `pt-BR` como idioma separado; `pt` é um modelo só, treinado
predominantemente em português europeu. Não há configuração que resolva.

**Não vamos tentar consertar por pós-processamento.** Trocar `correcto`→`correto` por tabela
resolveria a ortografia e deixaria a colocação pronominal intacta — meia solução que dá
aparência de solução. Fica registrado como limitação conhecida: a tradução serve para
**conferir sentido**, que é o que foi pedido, e o texto sai compreensível. Se ler como
português de Portugal incomodar no uso diário, o caminho é LLM local (rejeitado acima), não
remendo de string.
- **A reescrita natural continua ausente.** Esta ADR resolve metade do que a 0004 perdeu.
  Saber que `I have sent the report to you` soa estrangeiro ainda exige um modelo que
  entenda a frase. Decisão segue aberta.
