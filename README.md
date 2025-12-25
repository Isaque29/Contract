This repository demonstrates an integration of the Monogame framework with the Moonsharp library, allowing you to dynamically define cached values accessible in C# code.

The Lua manifest does:
```lua
return { version = "1.0"}
```

While to retrieve this value, we can call:
```csharp
Loader.Load<string>("version")
```
