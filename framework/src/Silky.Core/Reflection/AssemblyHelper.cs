using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using Silky.Core.Extensions.Collections.Generic;

namespace Silky.Core.Reflection;

public static class AssemblyHelper
{
    private static string AssemblySkipLoadingPattern { get; set; } =
        "^System|^mscorlib|^Microsoft|^AjaxControlToolkit|^Antlr3|^Autofac|^AutoMapper|^Castle|^ComponentArt|^CppCodeProvider|^DotNetOpenAuth|^EntityFramework|^EPPlus|^FluentValidation|^ImageResizer|^itextsharp|^log4net|^MaxMind|^MbUnit|^MiniProfiler|^Mono.Math|^MvcContrib|^Newtonsoft|^NHibernate|^nunit|^Org.Mentalis|^PerlRegex|^QuickGraph|^Recaptcha|^Remotion|^RestSharp|^Rhino|^Telerik|^Iesi|^TestDriven|^TestFu|^UserAgentStringLibrary|^VJSharpCodeProvider|^WebActivator|^WebDev|^WebGrease|^netstandard|^xunit";

    private static string AssemblyRestrictToLoadingPattern { get; set; } = ".*";

    public static List<Assembly> LoadAssemblies(string folderPath, SearchOption searchOption)
    {
        return GetAssemblyFiles(folderPath, searchOption)
            .Select(AssemblyLoadContext.Default.LoadFromAssemblyPath)
            .ToList();
    }

    public static IEnumerable<string> GetAssemblyFiles(string folderPath, SearchOption searchOption)
    {
        return Directory
                .EnumerateFiles(folderPath, "*.*", searchOption)
                .Where(p => p.EndsWith(".dll") || p.EndsWith(".exe"))
                .Select(p => Path.GetFullPath(p))
            ;
    }

    // public static IEnumerable<Assembly> GetAssemblies(string folderPath, SearchOption searchOption,
    //     bool skipLoadingSystem = true)
    // {
    //     var assemblyFiles = GetAssemblyFiles(folderPath, searchOption);
    //     var assemblies = assemblyFiles
    //         .Select(AssemblyLoadContext.Default.LoadFromAssemblyPath)
    //         .WhereIf(skipLoadingSystem, p => Matches(p.FullName));
    //     return assemblies;
    // }

    public static bool Matches(string assemblyFullName)
    {
        return !Matches(assemblyFullName, AssemblySkipLoadingPattern)
               && Matches(assemblyFullName, AssemblyRestrictToLoadingPattern);
    }

    public static bool Matches(string assemblyFullName, string pattern)
    {
        return Regex.IsMatch(assemblyFullName, pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public static IReadOnlyList<Type> GetAllTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types;
        }
    }
}