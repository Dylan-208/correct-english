# ADR 0006 — Gramática com LanguageTool em contêiner, e a fronteira entre L0 e L1

- **Data:** 2026-08-27
- **Status:** aceita
- **Contexto anterior:** [ADR 0002](0002-motor-de-correcao.md), [ADR 0004](0004-camada-l2-sem-custo-recorrente.md)

## Contexto

A ADR 0002 previu a camada L1 (gramática por regras) mas deixou aberta a forma de
distribuição, com três candidatos: exigir Java 17 instalado, embutir um JRE recortado de
~50 MB no instalador, ou Docker.

A ADR 0005 resolveu isso sem querer: o Docker já é dependência opcional do projeto por
causa do LibreTranslate. Acrescentar um segundo serviço ao mesmo `docker-compose.yml` custa
quase nada e elimina a decisão sobre Java por completo — nada de JRE, nada de instalar Java.

## Decisões

### 1. LanguageTool em contêiner, imagem `erikvl87/languagetool`

Em `localhost:8010`, `en-US`, heap limitado a 1 GB. A imagem oficial `languagetool/languagetool`
não existe no registro público; `erikvl87/languagetool` é a imagem mantida pela comunidade e
foi verificada antes de ser adotada.

**Sem os dados de n-grama**, que são vários GB e servem principalmente para pares
confundíveis (`their`/`there`, `its`/`it's`). Se essa classe de erro se mostrar frequente no
uso real, baixar os n-gramas é a primeira coisa a reconsiderar.

### 2. A fronteira entre L0 e L1 é conceitual, e só uma família de regras é descartada

O LanguageTool também corrige ortografia, então há sobreposição com a camada L0. A regra:

> **L0 é dona de "palavra que não existe no dicionário". L1 é dona de "palavra que existe
> mas está errada no contexto".**

Na prática isso significa descartar **apenas** as regras com prefixo `MORFOLOGIK` — o
corretor ortográfico do LanguageTool. Ela é também a única regra dele que sublinharia
`getUserById` ou `Dylan-208`, falso positivo que o tokenizador da L0 já sabe evitar e que
tem 26 testes protegendo.

**Errei isto na primeira tentativa**, e vale registrar como o erro foi encontrado. O filtro
original descartava também tudo com `issueType: misspelling` ou categoria de ortografia.
Ao testar contra o servidor real, apareceu isto:

```
[MISC / EN_A_VS_AN / misspelling]  'a' -> 'an'
```

O LanguageTool rotula erro de artigo (`a apple` → `an apple`) como `issueType: misspelling`
e categoria `MISC`. O filtro estava jogando fora exatamente a classe de erro que a camada L1
existe para pegar. **Só o prefixo da regra é confiável**; `issueType` e `category` não são.

O risco assumido ao filtrar tão pouco: alguma outra regra do LanguageTool pode disparar em
código ou URL. Há um teste de integração contra o servidor real
(`Nao_produz_falso_positivo_em_texto_tecnico_real`) que guarda essa fronteira com uma frase
contendo URL do GitHub, `getUserById`, e-mail, `@menção`, `user_id`, `API` e `don't`.

### 3. As camadas são encadeadas, não fundidas

`PipelineCorrectionProvider` passa a saída de uma camada como entrada da seguinte.

O motivo é concreto, não estético: duas camadas analisando o **mesmo** texto produzem
deslocamentos calculados sobre a mesma base, e aplicar os dois conjuntos corromperia o
texto — a segunda substituição usaria posições de antes da primeira. Encadeando, cada camada
recebe um texto já consistente e o problema **desaparece** em vez de precisar ser resolvido.

O preço: a L1 nunca vê o erro de ortografia que a L0 consertou. Isso é desejável — gramática
avaliada sobre texto com typo produz ruído.

O mesmo raciocínio, dentro da própria L1: quando duas regras apontam o mesmo trecho, a de
maior deslocamento é descartada.

### 4. As mensagens das regras são traduzidas, com cache

É a estratégia prevista na ADR 0004, agora possível porque o LibreTranslate já está de pé.
As mensagens do LanguageTool vêm em inglês; o provedor traduz e guarda em
`ConcurrentDictionary` por texto original. O conjunto de regras é finito e os erros de uma
pessoa se repetem, então depois de alguns dias quase toda mensagem vem do cache de graça.

Falha na tradução mantém a mensagem em inglês — degradação, não erro.

## Consequências

- A camada L1 é opcional como a tradução: contêiner desligado significa lista de problemas
  vazia, e a ortografia segue funcionando. A disponibilidade é verificada por chamada, então
  subir o Docker com o app aberto tem efeito imediato.
- Consumo em repouso: ~1 GB de RAM para o LanguageTool, além do LibreTranslate. Numa máquina
  de 16 GB é aceitável, mas não é grátis — é o argumento mais forte para a L1 continuar
  opcional em vez de virar obrigatória.
- **A reescrita natural continua ausente.** A L1 sabe que `I has a car` está errado, mas não
  que `I have sent the report to you` está correto e soa estrangeiro. Terceira ADR seguida a
  registrar isso; segue sendo o único item que exige um modelo de linguagem.
