using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace MoonSpeak
{
    public static class TypeMaker
    {
        public static AssemblyBuilder makeAssembly(string name)
        {
            var assemblyName = new AssemblyName(name);
            return AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave, @"C:\users\pcl");
        }

        public static ModuleBuilder makeModule(string name)
        {
            return makeModule(makeAssembly(name), name);
        }

        public static ModuleBuilder makeModule(AssemblyBuilder assembly, string name, string outputFile=null) {
            if (outputFile != null)
            {
                return assembly.DefineDynamicModule(assembly.FullName, outputFile);
            } else
            {
                return assembly.DefineDynamicModule(assembly.FullName);
            }
        }

        public static Type makeType(Script script, ModuleBuilder module, Type baseType, string typeName, Table delegates)
        {
            var existingType = module.GetTypes().Where(m => m.FullName == typeName).FirstOrDefault();
            if(existingType != null)
            {
                //Verse.Log.Message("Type " + typeName + " already exists, updating delegate table.");
                existingType.GetField("_moonSpeakDelegates").SetValue(null, delegates);
                return existingType;
            }
            var type = module.DefineType(typeName);
            type.SetParent(baseType);
            var delegateField = type.DefineField("_moonSpeakDelegates", typeof(Table), FieldAttributes.Public | FieldAttributes.Static);
            var scriptField = type.DefineField("_moonSpeakScript", typeof(Script), FieldAttributes.Public | FieldAttributes.Static);

            foreach (var del in delegates.Pairs)
            {
                var methodName = del.Key.String;
                //Verse.Log.Message("Creating method " + typeName + "." + methodName);
                var methodDelegate = del.Value;
                var baseMethod = baseType.GetMethod(methodName);
                var parameters = baseMethod.GetParameters();
                var paramTypes = parameters.Select(x => x.ParameterType).ToArray();
                var returnType = baseMethod.ReturnParameter.ParameterType;
                var method = type.DefineMethod(methodName,
                    MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                    CallingConventions.Standard | CallingConventions.HasThis,
                    returnType, paramTypes);
                var gen = method.GetILGenerator();
                int refParamCount = 0;
                int lastRefParamIndex = -1;
                for(int i = 0; i < parameters.Length; ++i)
                {
                    if(paramTypes[i].IsByRef)
                    {
                        refParamCount += 1;
                        lastRefParamIndex = i;
                    }
                }

                bool isVoid = returnType == typeof(void);
                bool isVoidWithSingleRef = isVoid && (refParamCount == 1);

                if (isVoidWithSingleRef)
                {
                    // We need the ref on the stack to store it later
                    gen.Emit(OpCodes.Ldarg, lastRefParamIndex+1); // +1 because arg_0 is `this`
                }

                // return script.Call( delegates[name], dynValues ).ToObject(returnType);
                // script.
                gen.Emit(OpCodes.Ldsfld, scriptField);

                // delegates[name]
                gen.Emit(OpCodes.Ldsfld, delegateField);
                gen.Emit(OpCodes.Ldstr, methodName);
                gen.Emit(OpCodes.Callvirt, typeof(Table).GetMethod("Get", new Type[] { typeof(string) }));
                // allocate dynValues
                gen.Emit(OpCodes.Ldc_I4, parameters.Length+1);
                gen.Emit(OpCodes.Newarr, typeof(DynValue));

                // pass 'this'
                gen.Emit(OpCodes.Dup);
                gen.Emit(OpCodes.Ldc_I4_0);
                gen.Emit(OpCodes.Ldsfld, scriptField);
                gen.Emit(OpCodes.Ldarg_0);
                gen.Emit(OpCodes.Call, typeof(DynValue).GetMethod("FromObject"));
                gen.Emit(OpCodes.Stelem_Ref);

                // foreach(o in params) { dynValues.Add( DynValue.FromObject(script, o) ); }
                for (var i = 0; i < parameters.Length; i++)
                {
                    var paramType = paramTypes[i];

                    gen.Emit(OpCodes.Dup);            // Duplicate array reference
                    gen.Emit(OpCodes.Ldc_I4, i + 1);  // Push index for stelem call at the end

                    // Convert argument to DynValue
                    gen.Emit(OpCodes.Ldsfld, scriptField);
                    gen.Emit(OpCodes.Ldarg, i + 1);

                    // Load value for reference args
                    if (paramType.IsByRef)
                    {
                        gen.Emit(OpCodes.Ldind_Ref);
                        paramType = paramType.GetElementType();
                    }

                    // Convert native types to object (int -> System.Int32, etc)
                    if (paramType.IsValueType)
                    {
                        gen.Emit(OpCodes.Box, paramType);
                    }

                    // Convert object to DynValue
                    gen.Emit(OpCodes.Call, typeof(DynValue).GetMethod("FromObject"));

                    // Store the DynValue in the array
                    gen.Emit(OpCodes.Stelem_Ref);
                }

                // Call
                gen.Emit(OpCodes.Callvirt, typeof(Script).GetMethod("Call", new Type[] { typeof(DynValue), typeof(DynValue[]) }));

                // Unpack multi return to refs if any args were ref/out
                if (!isVoidWithSingleRef && refParamCount > 0)
                {
                    // No swap instruction, so we have to use a local :(
                    gen.DeclareLocal(typeof(DynValue[]));

                    // Unpack tuple
                    gen.Emit(OpCodes.Callvirt, typeof(DynValue).GetProperty("Tuple").GetGetMethod());
                    gen.Emit(OpCodes.Stloc_0);
                    int tupleIndex = 0;

                    // Pull return out of 0th slot
                    if (!isVoid)
                    {
                        gen.Emit(OpCodes.Ldloc_0);
                        gen.Emit(OpCodes.Ldc_I4, 0);
                        gen.Emit(OpCodes.Ldelem_Ref);
                        tupleIndex += 1;
                    }

                    for (var i = 0; i < parameters.Length; i++)
                    {
                        if (!paramTypes[i].IsByRef) continue;
                        gen.Emit(OpCodes.Ldarg, i + 1); // index for Stind_ref below

                        // Load DynValue from array
                        gen.Emit(OpCodes.Ldloc_0);
                        gen.Emit(OpCodes.Ldc_I4, tupleIndex);
                        gen.Emit(OpCodes.Ldelem_Ref);

                        // Convert DynValue to correct type
                        gen.Emit(OpCodes.Call, DynValueToObjectMethodFor(paramTypes[i].GetElementType()));

                        // Store
                        gen.Emit(OpCodes.Stind_Ref);

                        tupleIndex += 1;
                    }

                }

                // Return
                if (isVoid)
                {
                    if (isVoidWithSingleRef)
                    {
                        // Convert DynValue to correct type
                        gen.Emit(OpCodes.Call, DynValueToObjectMethodFor(paramTypes[lastRefParamIndex].GetElementType()));
                        // Assign returned value to ref (which was pushed at the very beginning)
                        gen.Emit(OpCodes.Stind_Ref);
                    }
                    else
                    {
                        // Just remove result from script.Call from the stack
                        gen.Emit(OpCodes.Pop);
                    }
                }
                else
                {
                    // Convert DynValue to expected return type
                    gen.Emit(OpCodes.Call, DynValueToObjectMethodFor(returnType));
                }
                gen.Emit(OpCodes.Ret);

                var baseMethodProxy = type.DefineMethod("base_" + methodName,
                    MethodAttributes.Public | MethodAttributes.HideBySig, // must be public, call is from lua, not us
                    CallingConventions.Standard | CallingConventions.HasThis,
                    returnType, paramTypes);
                var gen2 = baseMethodProxy.GetILGenerator();
                gen2.Emit(OpCodes.Ldarg_0);
                for(var i = 0; i < parameters.Length; ++i)
                {
                    gen2.Emit(OpCodes.Ldarg, i + 1);
                }
                gen2.Emit(OpCodes.Call, baseMethod);
                gen2.Emit(OpCodes.Ret);
            }

            var result = type.CreateType();
            result.GetField("_moonSpeakDelegates").SetValue(null, delegates);
            result.GetField("_moonSpeakScript").SetValue(null, script);

            return result;
        }

        public static MethodInfo DynValueToObjectMethodFor(Type type)
        {
            return typeof(DynValue).GetMethods().Where(t => t.Name == "ToObject" && t.IsGenericMethod).First().MakeGenericMethod(type);
        }
    }
}
