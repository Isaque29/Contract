using System.Collections;
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
    private static Script? _script;
    private static Table? _rootTable;
    private static string _loadedFile = string.Empty;
    private static readonly Dictionary<string, DynValue> _moduleCache = new(StringComparer.OrdinalIgnoreCase);

    private static T Load<T>(string path, bool throwIfMissing, T? defaultValue)
    {
        if (_script == null || _rootTable == null)
            throw new InvalidOperationException("Scripting runtime not initialized.");

        DynValue dv = GetDynValueByPath(path);
        if (dv.IsNil())
        {
            if (throwIfMissing)
                throw new KeyNotFoundException($"Key not found in script data: '{path}'");
            return defaultValue!;
        }

        object? converted = ConvertDynValueToType(dv, typeof(T), _script);
        if (converted == null)
        {
            if (throwIfMissing)
                throw new InvalidCastException($"Could not convert value at '{path}' to type {typeof(T).FullName}.");
            return defaultValue!;
        }

        return (T)converted;
    }

    private static DynValue TraverseTable(Table start, string[] parts)
    {
        if (start == null) return DynValue.Nil;

        Table t = start;
        for (int i = 0; i < parts.Length; i++)
        {
            if (t == null) return DynValue.Nil;
            string key = parts[i];

            DynValue next = t.Get(key);
            if (next.IsNil())
            {
                next = t.Get(key.ToLowerInvariant());
                if (next.IsNil()) next = t.Get(key.ToUpperInvariant());
            }

            if (next.IsNil()) return DynValue.Nil;
            if (i == parts.Length - 1) return next;
            if (next.Type != DataType.Table) return DynValue.Nil;
            t = next.Table;
        }
        return DynValue.Nil;
    }

    /// <summary>
    /// Implementation of require() that loads modules from Content/Lua.
    /// Supports paths like "folder/module" or "module". Looks for module.lua and module/init.lua.
    /// Caches modules in _moduleCache for behavior similar to package.loaded.
    /// </summary>
    private static DynValue RequireModule(string moduleName)
    {
        if (_script == null) throw new InvalidOperationException("Script not initialized");
        if (string.IsNullOrEmpty(moduleName)) return DynValue.Nil;
        if (_moduleCache.TryGetValue(moduleName, out var cached)) return cached;

        try
        {
            var glob = _script.Globals.Get(moduleName);
            if (!glob.IsNil())
            {
                _moduleCache[moduleName] = glob;
                return glob;
            }
        }
        catch
        {
        }

        string normalized = moduleName.Replace('.', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        string baseLua = Path.Combine("Mods");

        var candidates = new List<string>
        {
            Path.Combine(baseLua, normalized + ".lua"),
            Path.Combine(baseLua, "defs", normalized + ".lua"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                DynValue result = _script.DoFile(candidate);
                _moduleCache[moduleName] = result ?? DynValue.Nil;
                return result ?? DynValue.Nil;
            }
        }

        _moduleCache[moduleName] = DynValue.Nil;
        return DynValue.Nil;
    }

    #region Conversion Helpers

    private static object? ConvertDynValueToType(DynValue dv, Type targetType, Script script)
    {
        if (dv.IsNil()) return null;

        Type? underlying = Nullable.GetUnderlyingType(targetType);
        if (underlying != null) return ConvertDynValueToType(dv, underlying, script);

        switch (dv.Type)
        {
            case DataType.String:
                if (targetType == typeof(string)) return dv.String;
                if (targetType.IsEnum) return Enum.Parse(targetType, dv.String!, true);
                return ChangeTypeSafely(dv.String!, targetType);

            case DataType.Number:
                if (targetType.IsEnum)
                    return Enum.ToObject(targetType, Convert.ToInt32(dv.Number));
                return ChangeTypeSafely(dv.Number, targetType);

            case DataType.Boolean:
                return ChangeTypeSafely(dv.Boolean, targetType);

            case DataType.Table:
                return MapTableToType(dv.Table, targetType, script);

            case DataType.UserData:
                return dv.UserData.Object;

            default:
                return null;
        }
    }

    private static object? ChangeTypeSafely(object value, Type targetType)
    {
        try{
            if (targetType.IsAssignableFrom(value.GetType())) return value;
            return Convert.ChangeType(value, targetType);
        }
        catch{
            return null;
        }
    }

    private static object? MapTableToType(Table table, Type targetType, Script script)
    {
        if (targetType == typeof(object))
            return MapTableToDictionary(table, typeof(object), script);

        Type? nullableUnderlying = Nullable.GetUnderlyingType(targetType);
        if (nullableUnderlying != null)
            return MapTableToType(table, nullableUnderlying, script);

        if (IsDictionary(targetType, out Type? keyT, out Type? valT) && keyT == typeof(string))
            return MapTableToDictionary(table, valT ?? typeof(object), script);

        if (IsListLike(targetType, out Type? itemType))
            return MapTableToList(table, itemType ?? typeof(object), targetType, script);

        if (targetType.IsClass || (targetType.IsValueType && !targetType.IsPrimitive && !targetType.IsEnum))
        {
            object inst = Activator.CreateInstance(targetType)!;
            foreach (var prop in targetType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!prop.CanWrite) continue;
                DynValue val = GetTableValueByKey(table, prop.Name);
                if (val.IsNil()) continue;
                object? conv = ConvertDynValueToType(val, prop.PropertyType, script);
                prop.SetValue(inst, conv);
            }

            foreach (var field in targetType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                DynValue val = GetTableValueByKey(table, field.Name);
                if (val.IsNil()) continue;
                object? conv = ConvertDynValueToType(val, field.FieldType, script);
                field.SetValue(inst, conv);
            }
            return inst;
        }

        if (targetType.IsEnum)
        {
            DynValue maybe = GetTableValueByKey(table, "value");
            if (maybe.IsNil()) maybe = GetTableValueByKey(table, "_value");
            if (!maybe.IsNil())
            {
                object? cv = ConvertDynValueToType(maybe, Enum.GetUnderlyingType(targetType), script);
                if (cv != null) return Enum.ToObject(targetType, cv);
            }
        }

        object obj = table;
        if (obj != null && targetType.IsAssignableFrom(obj.GetType())) return obj;

        return null;
    }

    private static DynValue GetTableValueByKey(Table table, string key)
    {
        DynValue v = table.Get(key);
        if (!v.IsNil()) return v;
        v = table.Get(key.ToLowerInvariant());
        if (!v.IsNil()) return v;
        v = table.Get(key.ToUpperInvariant());
        if (!v.IsNil()) return v;
        return DynValue.Nil;
    }

    private static object MapTableToDictionary(Table table, Type valueType, Script script)
    {
        var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
        var dict = (IDictionary)Activator.CreateInstance(dictType)!;
        foreach (var pair in table.Pairs)
        {
            string key = pair.Key.ToPrintString();
            object? val = ConvertDynValueToType(pair.Value, valueType, script);
            dict.Add(key, val);
        }
        return dict;
    }

    private static object MapTableToList(Table table, Type itemType, Type targetType, Script script)
    {
        var listType = typeof(List<>).MakeGenericType(itemType);
        var list = (IList)Activator.CreateInstance(listType)!;
        long len = (long)table.Length;
        if (len == 0)
        {
            var numeric = new List<KeyValuePair<int, DynValue>>();
            foreach (var pair in table.Pairs)
            {
                if (pair.Key.Type == DataType.Number)
                    numeric.Add(new KeyValuePair<int, DynValue>((int)pair.Key.Number, pair.Value));
            }
            numeric.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var kv in numeric)
                list.Add(ConvertDynValueToType(kv.Value, itemType, script));
        }
        else
        {
            for (int i = 1; i <= len; i++)
            {
                DynValue v = table.Get(i);
                list.Add(ConvertDynValueToType(v, itemType, script));
            }
        }

        if (targetType.IsArray)
        {
            var arr = Array.CreateInstance(itemType, list.Count);
            for (int i = 0; i < list.Count; i++) arr.SetValue(list[i], i);
            return arr;
        }

        if (targetType.IsAssignableFrom(list.GetType())) return list;
        try
        {
            var ctor = targetType.GetConstructor(new[] { list.GetType() });
            if (ctor != null) return ctor.Invoke(new object[] { list });
        }
        catch { }

        return list;
    }

    private static bool IsListLike(Type t, out Type? itemType)
    {
        itemType = null;
        if (t.IsArray) { itemType = t.GetElementType(); return true; }
        if (t.IsGenericType)
        {
            var gen = t.GetGenericArguments()[0];
            if (typeof(IList<>).MakeGenericType(gen).IsAssignableFrom(t) || typeof(IEnumerable<>).MakeGenericType(gen).IsAssignableFrom(t))
            { itemType = gen; return true; }
        }
        return false;
    }

    private static bool IsDictionary(Type t, out Type? keyType, out Type? valType)
    {
        keyType = valType = null;
        if (!t.IsGenericType) return false;
        var gens = t.GetGenericArguments();
        if (gens.Length != 2) return false;
        var def = typeof(Dictionary<,>).MakeGenericType(gens[0], gens[1]);
        if (def.IsAssignableFrom(t))
        {
            keyType = gens[0];
            valType = gens[1];
            return true;
        }
        return false;
    }

    #endregion
}
