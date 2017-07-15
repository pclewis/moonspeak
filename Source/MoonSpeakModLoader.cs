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
    [StaticConstructorOnStartup]
    public static class MoonSpeakModLoader
    {
        static MoonSpeakModLoader()
        {
            UserData.RegistrationPolicy = MoonSharp.Interpreter.Interop.InteropRegistrationPolicy.Automatic;

            var sharedScriptLoader = new FileSystemScriptLoader();
            var sharedModulePaths = new string[0];
            sharedScriptLoader.ModulePaths = sharedModulePaths;
            
            foreach (var mod in LoadedModManager.RunningMods)
            {
                string baseLuaPath = Path.Combine(mod.RootDir, "Lua");
                string mainFilePath = Path.Combine(baseLuaPath, "main.lua");
                string sharedLuaPath = Path.Combine(baseLuaPath, "shared");

                if (!File.Exists(mainFilePath)) continue;

                Script script = new Script();

                // Set up script loaders
                var modScriptLoader = new FileSystemScriptLoader();
                modScriptLoader.ModulePaths = new string[] { Path.Combine(baseLuaPath, "/?.lua") };

                if (Directory.Exists(sharedLuaPath))
                {
                    Array.Resize(ref sharedModulePaths, sharedModulePaths.Length + 1);
                    sharedModulePaths[sharedModulePaths.Length - 1] = Path.Combine(sharedLuaPath, "/?.lua");
                }
                script.Options.ScriptLoader = new MoonSpeakScriptLoader( modScriptLoader, sharedScriptLoader );

                // Set up globals
                script.Globals["dostring"] = (Func<string, Table, string, DynValue>)(script.DoString);
                script.Globals["loadstring"] = (Func<string, Table, string, DynValue>)(script.LoadString);
                script.Globals["typeof"] = (Func<DynValue, Type>)TypeOf;

                // Capture print
                script.Options.DebugPrint = (s => Log.Message(s));

                // Run main!
                script.DoFile(mainFilePath);
            }
        }

        static Type TypeOf(DynValue v)
        {
            if (v.Type == DataType.UserData && v.UserData.Object == null)
            {
                return v.UserData.Descriptor.Type;
            } else
            {
                return v.ToObject().GetType();
            }
        }

        static void stuff()
        {
            var assemblyName = new AssemblyName("testing");
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            var module = assembly.DefineDynamicModule(assembly.FullName);
            var type = module.DefineType("Test");
            type.SetParent(typeof(RimWorld.MainTabWindow));

        }
    }
}
