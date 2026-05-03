# Third-party notices — CAVE AI PRO

This file summarizes third-party libraries and components included in the distribution or used to build the product. **Update it** whenever dependencies change.

*Last review (indicative): [FILL IN date]*

---

## 1. .NET Runtime & Windows Presentation Foundation (WPF)

A **self-contained** distribution includes components of **.NET** and the Windows desktop stack. Microsoft’s terms apply to the software and redistributables.

- Information: [Microsoft .NET Library License](https://dotnet.microsoft.com/en-us/platform/license)  
- [Microsoft Software License Terms](https://www.microsoft.com/en-us/useterms) (general)

[FILL IN: .NET target of the project, e.g. net8.0-windows]

---

## 2. CommunityToolkit.Mvvm

- **Package:** `CommunityToolkit.Mvvm` (version in `CaveAiProForWindows.csproj`: [FILL IN from `dotnet list package`])  
- **License:** MIT  
- **Repository:** <https://github.com/CommunityToolkit/dotnet>

Full MIT text: see [07-OPEN-SOURCE-LICENSES.md](07-OPEN-SOURCE-LICENSES.md) or the package repository.

---

## 3. Build tools (not embedded in the end-user MSI)

- **WiX Toolset** — MSI authoring: <https://github.com/wixtoolset/wix> (WiX license and related)  
- **.NET SDK** — build: Microsoft terms

---

## 4. Fonts / logos

[FILL IN: if you use a third-party font with a separate license, record it here.]

---

## Command to refresh the dependency table

```bat
dotnet list "src\CaveAiProForWindows\CaveAiProForWindows.csproj" package --include-transitive
```
