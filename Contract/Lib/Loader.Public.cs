using System;
using System.IO;
using MoonSharp.Interpreter;
namespace Contract.Lib;

/// <summary>
/// Public class Loader that provides:
/// - Initialization and reloading of a main Lua file (default /Mod/manifest.lua)
/// - Simplified access to nested values ​​via Loader.Load<T>("a.b.c")
/// - TryLoad<T>() and Load<T>(path, defaultValue)
/// - Guaranteed require() functionality for modules in Content/Lua (with caching similar to package.loaded)
/// - Reading Lua source code in /Mod
/// - Version lookup (GetVersion)
///
/// Design notes:
/// - Require resolves modules by querying /Mod/<name>.lua and /Mod/<name>/manifest.lua
/// - Modules are cached in memory in the internal _moduleCache dictionary
/// - The implementation avoids relying on MoonSharp's FileSystemScriptLoader. providing predictable behavior
/// regardless of how the project is distributed.
/// </summary>
public static partial class Loader
{
    /// <summary>
    /// Initializes the Lua runtime by loading the specified file (default: modkit.lua in Content/Lua).
    /// Registers the require(name) function that searches for modules in Content/Lua.
    /// </summary>
    public static void Initialize(string rootDirectory, bool forced = false)
    {
        if (!forced && _script != null && string.Equals(_loadedFile, "manifest.lua", StringComparison.OrdinalIgnoreCase))
            return;

        string fullPath = Path.Combine(rootDirectory, "manifest.lua");
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Lua file not found: {fullPath}", fullPath);

        _script = new Script(CoreModules.Preset_SoftSandbox);
        _script.Globals.Set("require", DynValue.NewCallback((ctx, args) =>
        {
            if (args == null || args.Count == 0) return DynValue.Nil;
            string modName = args[0].CastToString();
            return RequireModule(modName);
        }));

        DynValue dv = _script.DoFile(fullPath);
        if (dv.Type != DataType.Table)
            throw new InvalidDataException($"Lua file must return a table at top level: {fullPath}");

        _rootTable = dv.Table;
        _loadedFile = "manifest.lua";
        _moduleCache.Clear();
    }
    /// <summary>
    /// Reloads the currently loaded script (or another if specified).
    /// </summary>
    public static void Reload(string? luaFileName = null) => Initialize(luaFileName ?? _loadedFile, forced: true);
    /// <summary>
    /// Directly reads a value from the path ("a/b/c" or "a.b.c").
    /// Throws KeyNotFoundException if it doesn't exist. Use the defaultValue overload for exception-free behavior.
    /// </summary>
    public static T Load<T>(string path) => Load<T>(path, throwIfMissing: true, defaultValue: default);
    public static T Load<T>(string path, T defaultValue) => Load<T>(path, throwIfMissing: false, defaultValue: defaultValue);

    public static bool TryLoad<T>(string path, out T? value)
    {
        try
        {
            value = Load<T>(path);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Allows to get the raw DynValue by path. Implicitly searches root.configs first.
    /// </summary>
    public static DynValue GetDynValueByPath(string path)
    {
        if (_script == null || _rootTable == null)
            throw new InvalidOperationException("Scripting runtime not initialized.");

        string[] parts = path.Split(new[] { '/', '.' }, StringSplitOptions.RemoveEmptyEntries);

        DynValue configs = _rootTable.Get("configs");
        if (!configs.IsNil() && configs.Type == DataType.Table)
        {
            DynValue found = TraverseTable(configs.Table, parts);
            if (!found.IsNil()) return found;
        }

        DynValue rootFound = TraverseTable(_rootTable, parts);
        return rootFound;
    }

    /// <summary>
    /// Reads the contents of a lua file within Content/Lua
    /// </summary>
    public static string? GetScriptSource(string relativePath)
    {
        string full = Path.Combine("Content", "Lua", relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return null;
        return File.ReadAllText(full);
    }
}
