# ADR 0003 — Caminho do aviso em tempo real

- **Data:** 2026-08-27
- **Status:** aceita, com reavaliação agendada

## Contexto

O pedido original era o sublinhado ondulado vermelho embaixo da palavra errada, igual ao que o
Windows já faz em português, funcionando em qualquer aplicativo.

Detectar o erro é a parte fácil (ver [ADR 0002](0002-motor-de-correcao.md)). A parte difícil é
**desenhar a ondinha dentro do campo de texto de outro programa**: isso exige saber a coordenada
em pixels de cada palavra na tela, e o Windows não entrega isso de forma confiável. É onde o
Grammarly concentra a maior parte da engenharia dele, e é a maior fonte de reclamação dos
usuários dele.

Cinco caminhos foram avaliados:

| | Caminho | Viabilidade | Esforço |
|---|---|---|---|
| **A** | Clipboard + atalho (`Ctrl+C ×3` → popup) | alta | 2–3 dias |
| **B** | Hook de teclado + aviso perto do caret | alta | ~1 semana |
| **C** | Extensão de navegador (ondinha real no DOM) | alta | ~1 semana |
| **D** | UI Automation + overlay topmost | média | semanas, aberto |
| **E** | TSF Text Service (DLL COM, como um IME) | baixa | meses |

## Decisão

**Implementar A e B. Não implementar C, D nem E agora.**

O aviso em tempo real virá do caminho **B**: o hook acumula a palavra sendo digitada, checa em
L0/L1, e mostra um indicador discreto perto do cursor — posição obtida via `GetGUIThreadInfo`.

**Não haverá sublinhado ondulado dentro do campo de texto na primeira versão.**

## Por quê

A + B entregam "me avisa quando eu erro, em qualquer aplicativo" com risco baixo e sem depender de
mapear pixels de palavra. O caminho A ainda resolve de graça o problema de ler o texto selecionado
de qualquer app: como o gatilho é `Ctrl+C`, o texto já está no clipboard.

C, D e E foram todos adiados por motivos diferentes:

- **C** (extensão) cobriria provavelmente 80% do inglês escrito — e com ondinha perfeita, porque no
  DOM se controla o render. Adiado por escopo, não por risco: é uma segunda base de código com seu
  próprio ciclo de publicação. **É a primeira coisa a reconsiderar.**
- **D** funciona em Word, WordPad e apps WPF/WinUI. Quebra em Electron, editores em canvas
  (Google Docs), VS Code, e a ondinha sai de lugar a cada scroll. Custo alto, resultado inconsistente.
- **E** é o jeito tecnicamente correto — o próprio Windows desenharia a ondinha, com posicionamento
  perfeito. Mas é C++ COM puro, precisa ser assinada, aparece na barra de idiomas e pode conflitar
  com IMEs instalados.

## Reavaliação

Depois de **um mês de uso diário real**, responder: o aviso perto do caret foi suficiente, ou fez
falta a ondinha de verdade? Se fez falta, **onde** exatamente?

- Se a resposta for "no navegador" → caminho **C**.
- Se for "no Word e no Outlook" → caminho **D**, mirando só esses dois apps, que são justamente
  onde o UI Automation é confiável.

A decisão vem do uso, não da especulação. Este ADR deve ser substituído nessa data.

## Restrições que este caminho impõe

O caminho B depende de um hook global de teclado, que é a mesma API de um keylogger. As proteções
listadas no README (lista de permissão por aplicativo, buffer só em RAM, detecção de campo de senha,
zero teclas enviadas para a rede) são **parte desta decisão**, não um item de backlog. Sem elas, o
caminho B não deve ser implementado.
