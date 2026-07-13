using System.Reflection;
using System.Reflection.Metadata;

var outputPath = Environment.GetEnvironmentVariable("ABI_PROBE_OUT");
if (!string.IsNullOrWhiteSpace(outputPath))
    Console.SetOut(new StreamWriter(outputPath) { AutoFlush = true });

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: AbiProbe <assembly> [referencing-assembly]");
    return 2;
}

var targetPath = Path.GetFullPath(args[0]);
AppDomain.CurrentDomain.AssemblyResolve += (_, eventArgs) =>
{
    var fileName = new AssemblyName(eventArgs.Name).Name + ".dll";
    foreach (var directory in new[] { Path.GetDirectoryName(targetPath)!, Environment.CurrentDirectory })
    {
        var candidate = Path.Combine(directory, fileName);
        if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
    }
    return null;
};
var assembly = Assembly.LoadFrom(targetPath);
Console.WriteLine($"ASSEMBLY {assembly.FullName}");

foreach (var type in assembly.GetExportedTypes().OrderBy(x => x.FullName, StringComparer.Ordinal))
{
    Console.WriteLine($"TYPE {TypeName(type)} {TypeFlags(type)}");
    foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                               .Where(IsAbiRelevant)
                               .OrderBy(MemberKey, StringComparer.Ordinal))
    {
        Console.WriteLine($"  {Describe(member)}");
    }
}

if (args.Length > 1)
{
    var referencingPath = Path.GetFullPath(args[1]);
    var references = ScanMemberReferences(referencingPath, assembly.GetName().Name!);
    Console.WriteLine($"REFERENCES {Path.GetFileName(referencingPath)} -> {assembly.GetName().Name}");
    foreach (var reference in references)
        Console.WriteLine($"  {reference}");
}

return 0;

static bool IsAbiRelevant(MemberInfo member) => member switch
{
    ConstructorInfo c => c.IsPublic || c.IsFamily || c.IsFamilyOrAssembly,
    MethodInfo m => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly,
    FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
    PropertyInfo p => (p.GetMethod?.IsPublic ?? false) || (p.SetMethod?.IsPublic ?? false) ||
                      (p.GetMethod?.IsFamily ?? false) || (p.SetMethod?.IsFamily ?? false),
    EventInfo e => (e.AddMethod?.IsPublic ?? false) || (e.AddMethod?.IsFamily ?? false),
    Type t => t.IsNestedPublic || t.IsNestedFamily,
    _ => false
};

static string Describe(MemberInfo member) => member switch
{
    ConstructorInfo c => $"CTOR {Visibility(c)} ({string.Join(",", c.GetParameters().Select(DescribeParameter))})",
    MethodInfo m => $"METHOD {Visibility(m)} {(m.IsStatic ? "static " : "")}{TypeName(m.ReturnType)} {m.Name}`{m.GetGenericArguments().Length}({string.Join(",", m.GetParameters().Select(DescribeParameter))})",
    FieldInfo f => $"FIELD {FieldVisibility(f)} {(f.IsStatic ? "static " : "")}{TypeName(f.FieldType)} {f.Name}",
    PropertyInfo p => $"PROPERTY {TypeName(p.PropertyType)} {p.Name} get={Accessor(p.GetMethod)} set={Accessor(p.SetMethod)}",
    EventInfo e => $"EVENT {TypeName(e.EventHandlerType!)} {e.Name}",
    Type t => $"NESTED {TypeName(t)}",
    _ => member.ToString() ?? member.Name
};

static string DescribeParameter(ParameterInfo p) => $"{(p.IsOut ? "out " : p.ParameterType.IsByRef ? "ref " : "")}{TypeName(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType)} {p.Name}";
static string Accessor(MethodInfo? m) => m is null ? "-" : Visibility(m);
static string Visibility(MethodBase m) => m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsFamilyOrAssembly ? "protected-internal" : "nonpublic";
static string FieldVisibility(FieldInfo f) => f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsFamilyOrAssembly ? "protected-internal" : "nonpublic";
static string TypeFlags(Type t) => $"base={TypeName(t.BaseType)} abstract={t.IsAbstract} enum={t.IsEnum} value={t.IsValueType}";
static string TypeName(Type? t)
{
    if (t is null) return "-";
    if (t.IsArray) return TypeName(t.GetElementType()) + "[]";
    if (t.IsGenericType) return (t.GetGenericTypeDefinition().FullName ?? t.Name) + "<" + string.Join(",", t.GetGenericArguments().Select(TypeName)) + ">";
    return t.FullName ?? t.Name;
}
static string MemberKey(MemberInfo m) => m.MemberType + ":" + m.Name + ":" + Describe(m);

static IReadOnlyList<string> ScanMemberReferences(string assemblyPath, string targetAssemblyName)
{
    // Runtime reflection cannot expose MemberRef rows. System.Reflection.Metadata is
    // part of the shared framework, so use it directly without adding dependencies.
    using var stream = File.OpenRead(assemblyPath);
    using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
    var md = pe.GetMetadataReader();
    var result = new SortedSet<string>(StringComparer.Ordinal);
    foreach (var handle in md.MemberReferences)
    {
        var mr = md.GetMemberReference(handle);
        if (mr.Parent.Kind != System.Reflection.Metadata.HandleKind.TypeReference) continue;
        var tr = md.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)mr.Parent);
        if (!BelongsToAssembly(md, tr.ResolutionScope, targetAssemblyName)) continue;
        result.Add($"{md.GetString(tr.Namespace)}.{md.GetString(tr.Name)}::{md.GetString(mr.Name)}");
    }
    foreach (var handle in md.TypeReferences)
    {
        var tr = md.GetTypeReference(handle);
        if (BelongsToAssembly(md, tr.ResolutionScope, targetAssemblyName))
            result.Add($"TYPE {md.GetString(tr.Namespace)}.{md.GetString(tr.Name)}");
    }
    return result.ToArray();
}

static bool BelongsToAssembly(System.Reflection.Metadata.MetadataReader md, System.Reflection.Metadata.EntityHandle scope, string target)
{
    while (!scope.IsNil)
    {
        if (scope.Kind == System.Reflection.Metadata.HandleKind.AssemblyReference)
            return md.GetString(md.GetAssemblyReference((System.Reflection.Metadata.AssemblyReferenceHandle)scope).Name) == target;
        if (scope.Kind != System.Reflection.Metadata.HandleKind.TypeReference) return false;
        scope = md.GetTypeReference((System.Reflection.Metadata.TypeReferenceHandle)scope).ResolutionScope;
    }
    return false;
}
