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

    [MoonSharpModule]
    public class MoonSpeakLuaFunctions
    {
        [MoonSharpModuleMethod(Name="typeof")]
        public static DynValue TypeOf(ScriptExecutionContext context, CallbackArguments args)
        {
            DynValue v = args[0];
            if (v.Type == DataType.UserData && v.UserData.Object == null) {
                return DynValue.FromObject(context.OwnerScript, v.UserData.Descriptor.Type);
            } else {
                return DynValue.FromObject(context.OwnerScript, v.ToObject().GetType());
            }
        }

        [MoonSharpModuleMethod(Name="class")]
        public static DynValue Class(ScriptExecutionContext context, CallbackArguments args)
        {
            Script script = context.OwnerScript;
            ModuleBuilder module = (ModuleBuilder)script.Globals["_moonSpeakModule"];
            Type type = TypeMaker.MakeType(
                script,
                module,
                args.AsUserData<Type>(1, "class", false),
                args.AsStringUsingMeta(context, 0, "class"),
                args.AsType(2, "class", DataType.Table).Table,
                args.Count < 4 ? null : args.AsType(3, "class", DataType.Function).Function);

            return DynValue.FromObject(script, type);
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

            var module = TypeMaker.MakeModule(mod.Name);

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
                script.Globals.RegisterModuleType<MoonSpeakLuaFunctions>();
                script.Globals["_moonSpeakModule"] = module;

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
    }

    public class MoonSpeak : Mod
    {
        public MoonSpeak(ModContentPack content) : base(content)
        {
            // do something here maybe
        }
    }
}
