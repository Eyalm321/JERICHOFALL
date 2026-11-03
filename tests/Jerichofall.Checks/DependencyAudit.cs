using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

internal static class DependencyAudit
{
    public static bool IsAllowedAssembly(string name) =>
        name is "Jerichofall" or "Jerichofall.Core" or "FNA" or "System" or "mscorlib" or "netstandard" or "WindowsBase" or "Microsoft.CSharp"
        || name.StartsWith("System.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal)
        || name.StartsWith("Microsoft.VisualBasic", StringComparison.Ordinal);

    public static void Run(string directory)
    {
        var assemblies=new SortedDictionary<string,string[]>();
        foreach(var path in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories))
        {
            if(Path.GetExtension(path) is not (".dll" or ".exe"))continue;
            using var stream=File.OpenRead(path);using var pe=new PEReader(stream);
            if(!pe.HasMetadata)continue;
            var metadata=pe.GetMetadataReader();if(!metadata.IsAssembly)continue;
            string name=metadata.GetString(metadata.GetAssemblyDefinition().Name);
            var references=metadata.AssemblyReferences.Select(h=>metadata.GetString(metadata.GetAssemblyReference(h).Name)).Order().ToArray();
            foreach(string candidate in references.Prepend(name))
                if(!IsAllowedAssembly(candidate))throw new InvalidDataException("Unapproved assembly dependency: "+candidate);
            assemblies.Add(name,references);
        }
        foreach(string required in new[]{"Jerichofall","Jerichofall.Core","FNA"})
            if(!assemblies.ContainsKey(required))throw new InvalidDataException("Missing assembly: "+required);
        if(assemblies["Jerichofall.Core"].Any(r=>!r.StartsWith("System.",StringComparison.Ordinal)))throw new InvalidDataException("Core must use only .NET libraries.");
        File.WriteAllText(Path.Combine(directory,"dependency-audit.json"),JsonSerializer.Serialize(new{passed=true,assemblies},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Dependency audit passed: {assemblies.Count} managed assemblies; all dependencies allowed.");
    }
}
