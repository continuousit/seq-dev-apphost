using System;
using System.IO;
using System.Reflection;

namespace Seq.Dev.AppHost
{
    static class AssemblyResolver
    {
        public static void Install(params string[] searchPaths)
        {
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                foreach (var path in searchPaths)
                {
                    var assemblyPath = Path.Combine(path, new AssemblyName(e.Name).Name + ".dll");
                    if (File.Exists(assemblyPath)) 
                        return Assembly.LoadFrom(assemblyPath);
                }

                return null;
            };
        }
    }
}