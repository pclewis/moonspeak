using MoonSharp.Interpreter;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;

namespace MoonSpeak
{
    static class TypeMaker
    {
        public static ModuleBuilder makeModule(string name)
        {
            var assemblyName = new AssemblyName(name);
            var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave);
            return assembly.DefineDynamicModule(assembly.FullName);
        }
        public static Type makeType(Script script, ModuleBuilder module, Type baseType, string name, Table delegates)
        {
            var type = module.DefineType(name);
            type.SetParent(baseType);
            var delegateField = type.DefineField("_moonSpeakDelegates", typeof(Table), FieldAttributes.Public | FieldAttributes.Static);
            var scriptField = type.DefineField("_moonSpeakScript", typeof(Script), FieldAttributes.Public | FieldAttributes.Static);

            foreach (var del in delegates.Pairs)
            {
                var methodName = del.Key.String;
                Console.WriteLine(methodName);
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

                bool hasRefParam = false;

                // return script.Call( delegates[name], dynValues ).ToObject(returnType);
                // script.
                gen.Emit(OpCodes.Ldsfld, scriptField);

                // delegates[name]
                gen.Emit(OpCodes.Ldsfld, delegateField);
                gen.Emit(OpCodes.Ldstr, methodName);
                gen.Emit(OpCodes.Callvirt, typeof(Table).GetMethod("Get", new Type[] { typeof(string) }));
                // allocate dynValues
                gen.Emit(OpCodes.Ldc_I4, parameters.Length);
                gen.Emit(OpCodes.Newarr, typeof(DynValue));

                // foreach(o in params) { dynValues.Add( DynValue.FromObject(script, o) ); }
                for (var i = 0; i < parameters.Length; i++)
                {
                    var paramType = paramTypes[i];

                    gen.Emit(OpCodes.Dup);        // Duplicate array reference
                    gen.Emit(OpCodes.Ldc_I4, i);  // Push index for stelem call at the end

                    // Convert argument to DynValue
                    gen.Emit(OpCodes.Ldsfld, scriptField);
                    gen.Emit(OpCodes.Ldarg, i + 1);

                    // Load value for reference args
                    if (paramType.IsByRef)
                    {
                        hasRefParam = true;
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
                if (hasRefParam)
                {
                    // No swap instruction, so we have to use a local :(
                    gen.DeclareLocal(typeof(DynValue[]));

                    // Unpack tuple
                    gen.Emit(OpCodes.Callvirt, typeof(DynValue).GetProperty("Tuple").GetGetMethod());
                    gen.Emit(OpCodes.Stloc_0);
                    int tupleIndex = 1;
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

                    // Pull return out of 0th slot
                    gen.Emit(OpCodes.Ldloc_0);
                    gen.Emit(OpCodes.Ldc_I4, 0);
                    gen.Emit(OpCodes.Ldelem_Ref);
                }

                // Return
                if (returnType == typeof(void))
                {
                    // no return, remove result from script.Call from the stack
                    gen.Emit(OpCodes.Pop);
                }
                else
                {
                    // Convert DynValue to expected return type
                    gen.Emit(OpCodes.Call, DynValueToObjectMethodFor(returnType));
                }
                gen.Emit(OpCodes.Ret);
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
