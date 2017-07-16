using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Verse;

namespace MoonSpeak
{
    public class InitializeModule : Def
    {
        public void LoadDataFromXmlCustom(System.Xml.XmlNode node)
        {
            var targetMod = node.Attributes["Module"].Value;
            // Game complains if we don't have a defName
            defName = "moonspeakmodule_" + targetMod;

            Log.Message("Loading MoonSpeak Module " + targetMod);
            foreach (var mod in LoadedModManager.RunningMods)
            {
                if (mod.Name == targetMod)
                {
                    Log.Message("Found mod");
                    MoonSpeakManager.LoadMod(mod);
                }
            }
        }
    }

    public class MoonSpeakManager
    {
        static ScriptLoaderBase sharedScriptLoader = new FileSystemScriptLoader();
        static ScriptLoaderBase typeLoader = new TypeResolvingScriptLoader();
        static string[] sharedModulePaths = new string[0];

        static MoonSpeakManager()
        {
            UserData.RegistrationPolicy = MoonSharp.Interpreter.Interop.InteropRegistrationPolicy.Automatic;
            sharedScriptLoader.ModulePaths = sharedModulePaths;
        }

        public static void LoadMod(ModContentPack mod)
        {
            string baseLuaPath = Path.Combine(mod.RootDir, "Lua");
            string mainFilePath = Path.Combine(baseLuaPath, "main.lua");
            string sharedLuaPath = Path.Combine(baseLuaPath, "shared");

            if (!File.Exists(mainFilePath))
            {
                Log.Error("Error loading Lua mod " + mod.Name + ": Couldn't find " + mainFilePath);
            }

            var module = TypeMaker.makeModule(mod.Name);

            try
            {
                Script script = new Script();

                // Set up script loaders
                var modScriptLoader = new FileSystemScriptLoader();
                modScriptLoader.ModulePaths = new string[] { Path.Combine(baseLuaPath, "?.lua") };

                if (Directory.Exists(sharedLuaPath))
                {
                    Array.Resize(ref sharedModulePaths, sharedModulePaths.Length + 1);
                    sharedModulePaths[sharedModulePaths.Length - 1] = Path.Combine(sharedLuaPath, "?.lua");
                }
                script.Options.ScriptLoader = new MultiScriptLoader(modScriptLoader, sharedScriptLoader, typeLoader);

                // Set up globals
                script.Globals["__moonspeak"] = new Table(script);
                script.Globals["__moonspeak", "types"] = new Table(script);
                script.Globals["typeof"] = (Func<DynValue, Type>)TypeOf;
                script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.makeType(script, module, baseType, name, delegates));

                // Capture print
                script.Options.DebugPrint = (s => Log.Message(s));

                // Run main!
                script.DoFile(mainFilePath);

                // Add assembly to mod
                mod.assemblies.loadedAssemblies.Add(module.Assembly);
            }
            catch (InterpreterException e)
            {
                Log.Message(e.DecoratedMessage);
                Log.Notify_Exception(e);
            }
            catch (Exception e)
            {
                Log.Notify_Exception(e);
            }
        }

        static Type TypeOf(DynValue v)
        {
            if (v.Type == DataType.UserData && v.UserData.Object == null)
            {
                return v.UserData.Descriptor.Type;
            }
            else
            {
                return v.ToObject().GetType();
            }
        }
    }

    public class MoonSpeak : Mod
    {
        public MoonSpeak(ModContentPack content) : base(content)
        {
            // do something here maybe
        }
    }
}
