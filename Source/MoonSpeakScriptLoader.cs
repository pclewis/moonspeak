using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace MoonSpeak
{
    /// <summary>
    /// Moonsharp ScriptLoader which returns .NET Types if none of the provided loaders return a result.
    /// </summary>
    class MoonSpeakScriptLoader : IScriptLoader
    {
        private ScriptLoaderBase[] loaders;
        
        public MoonSpeakScriptLoader(params ScriptLoaderBase[] loaders)
        {
            this.loaders = loaders;
        }

        /// <summary>
        /// ResolveModuleName is called in require() before anything is attempted to be loaded.
        /// The FileLoaders try their paths and return any where a file exists.
        /// If none exist, we look up the type and stash it in the global context.
        /// </summary>
        public string ResolveModuleName(string modname, Table globalContext)
        {
            // Try file loaders
            foreach (var loader in loaders)
            {
                var result = loader.ResolveFileName(modname, globalContext);
                if (result!=null) return result;
            }

            // See if we already loaded this type
            if (globalContext["__moonspeak", "types", modname] != null)
            {
                return modname;
            }

            // Look for matching Type
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(modname);
                if (type != null)
                {
                    globalContext["__moonspeak", "types", modname] = UserData.CreateStatic(type);
                    return modname;
                }
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

            // We should only make it into LoadFile if one of the loaders said a file existed,
            // or ResolveModuleName found and stashed a type, so all we have to do is return the
            // reference to the type. We can't do it directly, have to return Lua code.
            return "return __moonspeak.types['" + file + "']";
        }

        /// <summary>This is apparently deprecated and is only supposed to return filename.</summary> 
        public string ResolveFileName(string filename, Table globalContext)
        {
            return filename;
        }
    }
}
