// Read-only Roslyn compiler host. Never loads emitted or NinjaTrader assemblies.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public static class CompilerHost
{
    static string bin;
    static readonly HashSet<string> tooling = new HashSet<string>(StringComparer.Ordinal) {
        "Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp", "System.Collections.Immutable",
        "System.Reflection.Metadata", "System.Memory", "System.Runtime.CompilerServices.Unsafe",
        "System.Buffers", "System.Threading.Tasks.Extensions", "System.Numerics.Vectors",
        "System.Text.Encoding.CodePages", "System.ValueTuple"
    };
    // Keep Roslyn types out of Main so resolution is installed before JITting Compile.
    public static int Main(string[] args)
    {
        try {
            if(args.Length!=5)throw new ArgumentException("fixed compiler protocol required");
            bin=Path.GetFullPath(args[0]);
            var resolving=new HashSet<string>();
            AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=> {
                string name=new AssemblyName(e.Name).Name;
                if(!tooling.Contains(name)||!resolving.Add(name))throw new InvalidOperationException("dependency rejected");
                try {return Assembly.LoadFrom(Path.Combine(bin,name+".dll"));}
                finally {resolving.Remove(name);}
            };
            return Compile(args[1],args[2],args[3],args[4]);
        } catch(Exception ex) {
            File.WriteAllText("compiler-incident.txt",ex.ToString());
            Console.WriteLine("COMPILER_INCIDENT compiler-incident.txt");return 2;
        }
    }
    static int Compile(string mode,string sourcesFile,string referencesFile,string output)
    {
        if(mode!="full"&&mode!="fake"&&mode!="plain"&&mode!="simtest"&&mode!="fullsim")throw new ArgumentException("mode rejected");
        var parse=new CSharpParseOptions(LanguageVersion.CSharp7_3,
            preprocessorSymbols:mode=="fake"?new[]{"KINFX_OFFLINE_FAKE"}:mode=="simtest"?new[]{"KINFX_SIM_TEST","KINFX_OFFLINE_FAKE"}:mode=="fullsim"?new[]{"KINFX_SIM_TEST"}:new string[0]);
        var trees=File.ReadAllLines(sourcesFile).Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),parse,Path.GetFileName(p))).ToArray();
        // Run all gates before opening output. Active Unicode-escaped #define is decoded by Roslyn.
        if(mode=="full")foreach(var tree in trees) {
            if(tree.Options.PreprocessorSymbolNames.Contains("KINFX_OFFLINE_FAKE")||tree.Options.PreprocessorSymbolNames.Contains("KINFX_SIM_TEST"))throw new InvalidOperationException("fake option rejected");
            foreach(var d in tree.GetRoot().DescendantTrivia(null,true).Select(t=>t.GetStructure()).OfType<DefineDirectiveTriviaSyntax>())
                if(d.IsActive&&(d.Name.ValueText=="KINFX_OFFLINE_FAKE"||d.Name.ValueText=="KINFX_SIM_TEST"))throw new InvalidOperationException("fake directive rejected");
        }
        var refs=File.ReadAllLines(referencesFile).Select(p=>MetadataReference.CreateFromFile(p)).ToArray();
        if(mode!="full"&&mode!="fullsim")foreach(var p in File.ReadAllLines(referencesFile))
            if(!new[]{"mscorlib.dll","System.dll","System.Core.dll","System.Xml.dll","System.Xml.Linq.dll"}.Contains(Path.GetFileName(p),StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("runtime reference rejected");
        var options=new CSharpCompilationOptions(mode=="full"||mode=="fullsim"?OutputKind.DynamicallyLinkedLibrary:OutputKind.ConsoleApplication,
            optimizationLevel:OptimizationLevel.Debug, mainTypeName:mode=="full"||mode=="fullsim"?null:"CompilerCaseRunner", allowUnsafe:false);
        var c=CSharpCompilation.Create(Path.GetFileNameWithoutExtension(output),trees,refs,options);
        using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write)) {
            var result=c.Emit(stream);
            File.WriteAllLines("compiler-diagnostics.txt",result.Diagnostics.Select(d=>d.ToString()));
            Console.WriteLine(result.Success?"COMPILE_OK":"COMPILE_FAIL");
            return result.Success?0:1;
        }
    }
}
