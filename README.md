# Litmus 🧪

**Litmus** is a .NET global CLI tool designed to **generate unit test scaffolds** for your Blazor components. It’s smart enough to parse your component's structure and generate parameter declarations, `@ref` usage, and assertion templates to kickstart your testing process with minimal friction.

---

## ✨ Features

- 🔍 Parses `.razor` component files
- 🧪 Generates xUnit test class scaffolds
- 🧭 Detects component parameters and `@ref` usage
- 💡 Provides starter assertions to guide your tests
- ⚡ Built for Blazor, tested with bUnit
- 🛠️ 100% offline — no dependencies once installed

---

## 🚀 Installation

### 🛠️ From Local Package (For Development / Testing)

1. Build your `.nupkg`:
   ```bash
   dotnet pack -c Release
