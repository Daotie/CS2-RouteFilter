using System.Reflection;
using System.Runtime.Loader;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: ApiInspector <assembly> <filter>");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var filter = args[1];
var directory = Path.GetDirectoryName(assemblyPath)!;

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var dependency = Path.Combine(directory, name.Name + ".dll");
    return File.Exists(dependency) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(dependency) : null;
};

var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type[] types;
try
{
    types = assembly.GetTypes();
}
catch (ReflectionTypeLoadException exception)
{
    types = exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
}

foreach (var type in types.Where(type => (type.FullName ?? type.Name).Contains(filter, StringComparison.OrdinalIgnoreCase)).OrderBy(type => type.FullName))
{
    Console.WriteLine($"TYPE {FormatType(type)}");

    foreach (var field in Safe(() => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
        Console.WriteLine($"  FIELD {FieldVisibility(field)} {FormatType(field.FieldType)} {field.Name}");

    foreach (var property in Safe(() => type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
        Console.WriteLine($"  PROP {FormatType(property.PropertyType)} {property.Name}");

    foreach (var method in Safe(() => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)).OrderBy(method => method.Name))
    {
        var parameters = string.Join(", ", method.GetParameters().Select(parameter => $"{FormatType(parameter.ParameterType)} {parameter.Name}"));
        Console.WriteLine($"  METHOD {MethodVisibility(method)} {FormatType(method.ReturnType)} {method.Name}({parameters})");
    }
}

return 0;

static IEnumerable<T> Safe<T>(Func<IEnumerable<T>> action)
{
    try { return action(); }
    catch { return Array.Empty<T>(); }
}

static string FieldVisibility(FieldInfo field) => field.IsPublic ? "public" : field.IsPrivate ? "private" : "internal";
static string MethodVisibility(MethodBase method) => method.IsPublic ? "public" : method.IsPrivate ? "private" : "internal";
static string FormatType(Type type)
{
    if (type.IsByRef) return FormatType(type.GetElementType()!) + "&";
    if (type.IsPointer) return FormatType(type.GetElementType()!) + "*";
    if (type.IsArray) return FormatType(type.GetElementType()!) + "[]";
    if (!type.IsGenericType) return type.FullName ?? type.Name;
    var name = (type.GetGenericTypeDefinition().FullName ?? type.Name).Split('`')[0];
    return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(FormatType))}>";
}
