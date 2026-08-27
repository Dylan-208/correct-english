# ADR 0001 — Stack: C# / .NET 8 + WPF

- **Data:** 2026-08-27
- **Status:** aceita

## Contexto

Metade do trabalho deste app não é interface: é API crua do Windows — hook global de teclado
(`WH_KEYBOARD_LL`), captura e restauração do clipboard, guardar e devolver o foco de janela
(`HWND`), simular `Ctrl+V` (`SendInput`), e no futuro janelas topmost click-through e
UI Automation. A escolha de stack pesa mais aqui do que em um projeto normal.

Quatro opções foram avaliadas: C# / .NET 8 + WPF, Electron + TypeScript, Tauri 2 + Rust,
e Python + PySide6.

## Decisão

**C# / .NET 8 com WPF.**

## Por quê

- UI Automation está na biblioteca padrão (`System.Windows.Automation`). Nas outras stacks isso
  exige FFI ou um processo auxiliar em outra linguagem.
- Hook de teclado e janela de overlay são P/Invoke direto, sem camada intermediária.
- Hunspell tem port gerenciado maduro (`WeCantSpell.Hunspell`), sem binário nativo para embarcar.
- É a única das quatro em que o caminho do overlay nativo (ver [ADR 0003](0003-caminho-do-sublinhado.md))
  é sequer viável no futuro, sem reescrever nada.
- Publica como binário único.

## O que estamos aceitando em troca

- **Windows-only.** Aceitável: o produto é para Windows.
- **Popup dá mais trabalho** do que seria em HTML/CSS. O mockup em `docs/plano.html` vira
  referência visual, não código reaproveitável.
- **Exige instalar o .NET 8 SDK** (~1 GB), que não estava na máquina.

## Alternativa mais forte que foi rejeitada

**Electron + TypeScript.** Node já estava instalado, o popup seria literalmente o HTML do mockup,
e sairia do zero em um fim de semana. Rejeitada porque overlay com sublinhado e UI Automation
exigiriam um processo auxiliar em C# ou Rust de qualquer forma — ou seja, duas linguagens e dois
runtimes para chegar onde o .NET chega sozinho, pagando ~180 MB de instalador e ~250 MB de RAM
parado no caminho.
