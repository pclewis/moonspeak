using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MoonSpeak
{
    /// <summary>
    /// Script loader that returns static references to types.
    /// </summary>
    class TypeResolvingScriptLoader : ScriptLoaderBase
    {
        /// <summary>
        /// ResolveModuleName is called in require() before anything is attempted to be loaded.
        /// We look up the type and stash it in the global context.
        /// </summary>
        public override string ResolveModuleName(string modname, Table globalContext)
        {
            // See if we already loaded this type
            if (globalContext["__moonspeak", "types", modname] != null)
            {
                Verse.Log.Message("Type already loaded " + modname);
                return modname;
            }

            // Look for matching Type
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(modname);
                if (type != null)
                {
                    globalContext["__moonspeak", "types", modname] = UserData.CreateStatic(type);
                    Verse.Log.Message("Resolved type " + modname);
                    return modname;
                }
            }

            return null;
        }

        public override object LoadFile(string file, Table globalContext)
        {
            // We should only make it into LoadFile if ResolveModuleName found and stashed a type
            // so all we have to do is return the reference to the type. But we can't return it directly,
            // we have to return Lua code.
            Verse.Log.Message("Loading type " + file);
            return "return _G['__moonspeak']['types']['" + file + "']";
        }

        public override bool ScriptFileExists(string name)
        {
            // HACK: we know we're last in the multi loader, so just claim everything.
            // The only proper thing we could do is resolve the type all over again, cause we don't have globalContext here.
            return true;
        }
    }
}
