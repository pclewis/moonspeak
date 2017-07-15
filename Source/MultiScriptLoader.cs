using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace MoonSpeak
{
    /// <summary>
    /// Moonsharp ScriptLoader which tries multiple script loaders and uses the first successful one.
    /// </summary>
    class MultiScriptLoader : IScriptLoader
    {
        private ScriptLoaderBase[] loaders;
        
        public MultiScriptLoader(params ScriptLoaderBase[] loaders)
        {
            this.loaders = loaders;
        }

        public string ResolveModuleName(string modname, Table globalContext)
        {
            foreach (var loader in loaders)
            {
                var result = loader.ResolveModuleName(modname, globalContext);
                // We should probably add some tag to get back to the correct loader
                if (result!=null) return result;
            }

            return null;
        }

        public object LoadFile(string file, Table globalContext)
        {
            foreach (var loader in loaders)
            {
                if (loader.ScriptFileExists(file))
                {
                    return loader.LoadFile(file, globalContext);
                }
            }
            System.Diagnostics.Debug.Assert(false, "No script loaders claimed file requested by LoadFile.");
            return null;
        }

        public string ResolveFileName(string filename, Table globalContext)
        {
            return filename;
        }
    }
}
