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

        public static ModuleBuilder makeModule(AssemblyBuilder assembly, string name, string outputFile = null)
        {
            if (outputFile != null) {
                return assembly.DefineDynamicModule(assembly.FullName, outputFile);
            } else {
                return assembly.DefineDynamicModule(assembly.FullName);
            }
        }

        public static Type makeType(Script script, ModuleBuilder module, Type baseType, string typeName, Table delegates)
        {
            var tb = new LuaTypeBuilder(script, module, baseType, typeName, delegates);
            tb.AllSteps();
            return tb.Finish();
        }

        public static MethodInfo DynValueToObjectMethodFor(Type type)
        {
            return typeof(DynValue).GetMethods().Where(t => t.Name == "ToObject" && t.IsGenericMethod).First().MakeGenericMethod(type);
        }
    }


    public class LuaTypeBuilder
    {
        private Script script;
        private ModuleBuilder module;
        private Type baseType;
        private string typeName;
        private Table delegates;
        private TypeBuilder typeBuilder;
        private Type existingType;

        private FieldBuilder delegateField;
        private FieldBuilder scriptField;
        private FieldBuilder tableField;

        public LuaTypeBuilder(Script script, ModuleBuilder module, Type baseType, string typeName, Table delegates)
        {
            this.script = script;
            this.module = module;
            this.baseType = baseType;
            this.typeName = typeName;
            this.delegates = delegates;

            existingType = module.GetTypes().Where(m => m.FullName == typeName).FirstOrDefault();
            if (existingType != null) {
                existingType.GetField("_moonSpeakDelegates").SetValue(null, delegates);
            } else {
                typeBuilder = module.DefineType(typeName);
                typeBuilder.SetParent(baseType);
            }
        }

        public void AllSteps()
        {
            AddFields();
            DefineMethods();
        }

        public void DefineMethods()
        {
            if (typeBuilder != null) {
                foreach (var method in baseType.GetConstructors()) {
                    AddConstructor(method);
                }

                foreach (var method in baseType.GetMethods()) {
                    if (method.IsVirtual) {
                        AddBaseCallerMethod(method);
                        AddMethod(method, method.Name);
                    }
                }
            }
        }

        public Type Finish()
        {
            if (existingType == null) {
                existingType = typeBuilder.CreateType();
                existingType.GetField("_moonSpeakDelegates").SetValue(null, delegates);
                existingType.GetField("_moonSpeakScript").SetValue(null, script);
            }
            return existingType;
        }

        private void AddFields()
        {
            if (typeBuilder != null) {
                delegateField = typeBuilder.DefineField("_moonSpeakDelegates", typeof(Table), FieldAttributes.Public | FieldAttributes.Static);
                scriptField = typeBuilder.DefineField("_moonSpeakScript", typeof(Script), FieldAttributes.Public | FieldAttributes.Static);
                tableField = typeBuilder.DefineField("_moonSpeakTable", typeof(Table), FieldAttributes.Public);
            }
        }

        public void AddConstructor(ConstructorInfo baseConstructor)
        {
            var paramTypes = baseConstructor.GetParameters().Select(p => p.ParameterType).ToArray();
            var method = typeBuilder.DefineConstructor(
                baseConstructor.Attributes,
                baseConstructor.CallingConvention,
                paramTypes);
            var gen = method.GetILGenerator();
            EmitBaseCall(gen, baseConstructor);
            var skip = gen.DefineLabel();
            EmitDelegateCall(gen, "__new", paramTypes, skip); // return value on stack, or jumped to skip
            EmitUnpackRefs(gen, typeof(void), paramTypes); // nothing on stack since return type is void
            gen.MarkLabel(skip);
            gen.Emit(OpCodes.Ret);
        }

        public void AddMethod(MethodInfo baseMethod, string delegateName)
        {
            var paramTypes = baseMethod.GetParameters().Select(p => p.ParameterType).ToArray();
            var method = typeBuilder.DefineMethod(
                baseMethod.Name,
                baseMethod.Attributes &~ MethodAttributes.NewSlot,
                baseMethod.CallingConvention,
                baseMethod.ReturnType,
                paramTypes);
            var gen = method.GetILGenerator();
            var skipUnpack = gen.DefineLabel();
            var skipCallBase = gen.DefineLabel();
            EmitDelegateCall(gen, delegateName, paramTypes, skipUnpack); // return value on stack
            EmitUnpackRefs(gen, baseMethod.ReturnType, paramTypes); // return value on stack unless void
            EmitUnpackReturn(gen, baseMethod.ReturnType);
            gen.Emit(OpCodes.Br, skipCallBase);
            gen.MarkLabel(skipUnpack);
            EmitBaseCall(gen, baseMethod);
            gen.MarkLabel(skipCallBase);
            gen.Emit(OpCodes.Ret);
        }

        public void AddBaseCallerMethod(MethodInfo baseMethod)
        {
            var paramTypes = baseMethod.GetParameters().Select(p => p.ParameterType).ToArray();
            var method = typeBuilder.DefineMethod(
                "base_" + baseMethod.Name,
                baseMethod.Attributes, // should we remove Virtual? add NewSlot?
                baseMethod.CallingConvention,
                baseMethod.ReturnType,
                paramTypes);
            var gen = method.GetILGenerator();
            EmitBaseCall(gen, baseMethod);
            gen.Emit(OpCodes.Ret);
        }

        public static void EmitLoadAllArgs(ILGenerator gen, MethodBase baseMethod)
        {
            gen.Emit(OpCodes.Ldarg_0); // this
            for (int i = 0; i < baseMethod.GetParameters().Length; ++i)
                gen.Emit(OpCodes.Ldarg, i + 1);
        }

        public static void EmitBaseCall(ILGenerator gen, ConstructorInfo baseMethod)
        {
            EmitLoadAllArgs(gen, baseMethod);
            gen.Emit(OpCodes.Call, baseMethod);
        }

        public static void EmitBaseCall(ILGenerator gen, MethodInfo baseMethod)
        {
            EmitLoadAllArgs(gen, baseMethod);
            gen.Emit(OpCodes.Call, baseMethod);
        }

        public void EmitDelegateCall(ILGenerator gen, string methodName, Type[] paramTypes, Label skip)
        {
            // delegates[name]
            gen.Emit(OpCodes.Ldsfld, delegateField);
            gen.Emit(OpCodes.Ldstr, methodName);
            gen.Emit(OpCodes.Callvirt, typeof(Table).GetMethod("Get", new Type[] { typeof(string) }));

            // Skip if delegate doesn't exist
            var delVar = gen.DeclareLocal(typeof(DynValue));
            gen.Emit(OpCodes.Stloc, delVar);
            gen.Emit(OpCodes.Ldloc, delVar);
            gen.Emit(OpCodes.Brfalse, skip);

            // allocate dynValues
            gen.Emit(OpCodes.Ldc_I4, paramTypes.Length + 1);
            gen.Emit(OpCodes.Newarr, typeof(DynValue));
            var arrVar = gen.DeclareLocal(typeof(DynValue[]));
            gen.Emit(OpCodes.Stloc, arrVar);

            // pass 'this'
            gen.Emit(OpCodes.Ldloc, arrVar);
            gen.Emit(OpCodes.Ldc_I4_0);

            // DynValue.FromObject(script, this)
            gen.Emit(OpCodes.Ldsfld, scriptField);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Call, typeof(DynValue).GetMethod("FromObject"));

            gen.Emit(OpCodes.Stelem_Ref);

            // foreach(o in params) { dynValues.Add( DynValue.FromObject(script, o) ); }
            for (var i = 0; i < paramTypes.Length; i++) {
                var paramType = paramTypes[i];

                // Store the DynValue in the array
                gen.Emit(OpCodes.Ldloc, arrVar);
                gen.Emit(OpCodes.Ldc_I4, i + 1);

                // Convert argument to DynValue
                gen.Emit(OpCodes.Ldsfld, scriptField);
                gen.Emit(OpCodes.Ldarg, i + 1);

                // Load value for reference args
                if (paramType.IsByRef) {
                    gen.Emit(OpCodes.Ldind_Ref);
                    paramType = paramType.GetElementType();
                }

                // Convert native types to object (int -> System.Int32, etc)
                if (paramType.IsValueType) {
                    gen.Emit(OpCodes.Box, paramType);
                }

                // Convert object to DynValue
                gen.Emit(OpCodes.Call, typeof(DynValue).GetMethod("FromObject"));

                gen.Emit(OpCodes.Stelem_Ref);
            }

            gen.Emit(OpCodes.Ldsfld, scriptField);
            gen.Emit(OpCodes.Ldloc, delVar);
            gen.Emit(OpCodes.Ldloc, arrVar);

            // Call
            gen.Emit(OpCodes.Callvirt, typeof(Script).GetMethod("Call", new Type[] { typeof(DynValue), typeof(DynValue[]) }));
        }

        public static void EmitUnpackReturn(ILGenerator gen, Type returnType)
        {
            if (returnType != typeof(void)) {
                gen.Emit(OpCodes.Callvirt, TypeMaker.DynValueToObjectMethodFor(returnType));
            }
        }

        public static void EmitUnpackRefs(ILGenerator gen, Type returnType, Type[] paramTypes)
        {
            var refParams = paramTypes.Where(p => p.IsByRef);
            switch (refParams.Count()) {
                case 0:
                    return;

                case 1:
                    if (returnType == typeof(void)) {
                        // No swap instruction, so we have to use a local :(
                        var dynLocal = gen.DeclareLocal(typeof(DynValue));
                        gen.Emit(OpCodes.Stloc, dynLocal);

                        // Iterate all parameters because we need to know the argument number
                        for (var i = 0; i < paramTypes.Length; i++) {
                            if(paramTypes[i].IsByRef) {
                                gen.Emit(OpCodes.Ldarg, i + 1);
                                break;
                            }
                        }
                        // Convert DynValue to correct type
                        gen.Emit(OpCodes.Ldloc, dynLocal);
                        gen.Emit(OpCodes.Call, TypeMaker.DynValueToObjectMethodFor(refParams.First().GetElementType()));
                        // Assign returned value to ref
                        gen.Emit(OpCodes.Stind_Ref);
                        break;
                    } else {
                        goto default;
                    }

                default:
                    // No swap instruction, so we have to use a local :(
                    var arrayLocal = gen.DeclareLocal(typeof(DynValue[]));

                    // Unpack tuple
                    gen.Emit(OpCodes.Callvirt, typeof(DynValue).GetProperty("Tuple").GetGetMethod());
                    gen.Emit(OpCodes.Stloc, arrayLocal);
                    int tupleIndex = 0;

                    // Pull return out of 0th slot
                    if (returnType != typeof(void)) {
                        gen.Emit(OpCodes.Ldloc, arrayLocal);
                        gen.Emit(OpCodes.Ldc_I4, tupleIndex);
                        gen.Emit(OpCodes.Ldelem_Ref);
                        tupleIndex += 1;
                    }

                    // Iterate all parameters because we need to know the argument number
                    for (var i = 0; i < paramTypes.Length; i++) {
                        if (!paramTypes[i].IsByRef) {
                            continue;
                        }

                        gen.Emit(OpCodes.Ldarg, i + 1); // arg for Stind_ref below

                        // Load DynValue from array
                        gen.Emit(OpCodes.Ldloc, arrayLocal);
                        gen.Emit(OpCodes.Ldc_I4, tupleIndex);
                        gen.Emit(OpCodes.Ldelem_Ref);

                        // Convert DynValue to correct type
                        gen.Emit(OpCodes.Call, TypeMaker.DynValueToObjectMethodFor(paramTypes[i].GetElementType()));

                        // Store
                        gen.Emit(OpCodes.Stind_Ref);

                        tupleIndex += 1;
                    }
                    break;
            }
        }
    }
}
