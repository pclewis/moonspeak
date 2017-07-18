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
        public static AssemblyBuilder MakeAssembly(string name)
        {
            var assemblyName = new AssemblyName(name);
            return AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.RunAndSave, @"C:\users\pcl");
        }

        public static ModuleBuilder MakeModule(string name)
        {
            return MakeModule(MakeAssembly(name), name);
        }

        public static ModuleBuilder MakeModule(AssemblyBuilder assembly, string name, string outputFile = null)
        {
            if (outputFile != null) {
                return assembly.DefineDynamicModule(assembly.FullName, outputFile);
            } else {
                return assembly.DefineDynamicModule(assembly.FullName);
            }
        }

        public static Type MakeType(Script script, ModuleBuilder module, Type baseType, string typeName, Table delegates)
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
        private FieldBuilder indexField;
        private FieldBuilder newIndexField;
        private FieldBuilder tableField;           // instance table for 'self' in lua code

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
                if (baseType.GetConstructors().Length == 0) {
                    // If it's an abstract class it may not have a constructor
                    AddConstructor(typeof(Object).GetConstructor(Type.EmptyTypes));
                } else {
                    foreach (var method in baseType.GetConstructors()) {
                        AddConstructor(method);
                    }
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
                existingType.GetField(delegateField.Name).SetValue(null, delegates);
                existingType.GetField(scriptField.Name).SetValue(null, script);
                existingType.GetField(indexField.Name).SetValue(null,
                    script.DoString(@"return function(table,index)
                      local instance = getmetatable(table).instance
                      local result = instance._moonSpeakDelegates[index]
                      if result then return result end
                      return instance[index]
                    end"));
                existingType.GetField(newIndexField.Name).SetValue(null,
                    script.DoString(@"return function(table,index,value)
                      local instance = getmetatable(table).instance
                      local status = pcall(function() instance[index]=value end)
                      if not status then rawset(table,index,value) end
                    end"));
            }
            return existingType;
        }

        private void AddFields()
        {
            if (typeBuilder != null) {
                delegateField = typeBuilder.DefineField("_moonSpeakDelegates", typeof(Table), FieldAttributes.Public | FieldAttributes.Static);
                scriptField = typeBuilder.DefineField("_moonSpeakScript", typeof(Script), FieldAttributes.Public | FieldAttributes.Static);
                indexField = typeBuilder.DefineField("_moonSpeakIndex", typeof(DynValue), FieldAttributes.Public | FieldAttributes.Static);
                newIndexField = typeBuilder.DefineField("_moonSpeakNewIndex", typeof(DynValue), FieldAttributes.Public | FieldAttributes.Static);
                tableField = typeBuilder.DefineField("_moonSpeakTable", typeof(DynValue), FieldAttributes.Public);
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
            EmitTableInitializer(gen);
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
            gen.Emit(OpCodes.Ldloc, delVar);
            gen.Emit(OpCodes.Call, typeof(DynValue).GetMethod("IsNil"));
            gen.Emit(OpCodes.Brtrue, skip);

            // allocate dynValues
            gen.Emit(OpCodes.Ldc_I4, paramTypes.Length + 1);
            gen.Emit(OpCodes.Newarr, typeof(DynValue));
            var arrVar = gen.DeclareLocal(typeof(DynValue[]));
            gen.Emit(OpCodes.Stloc, arrVar);

            // arr[0] = self (our table)
            gen.Emit(OpCodes.Ldloc, arrVar);
            gen.Emit(OpCodes.Ldc_I4_0);
            gen.Emit(OpCodes.Ldarg_0);
            gen.Emit(OpCodes.Ldfld, tableField);
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
                    // Leave stack empty when returnType is void
                    if (returnType == typeof(void)) {
                        gen.Emit(OpCodes.Pop);
                    }
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

        public void EmitTableInitializer(ILGenerator gen)
        {
            var tableConstructor = typeof(Table).GetConstructor(new Type[] { typeof(Script) });
            var metaTableSet = typeof(Table).GetProperty("MetaTable").GetSetMethod();
            var tableSet = typeof(Table).GetMethod("Set", new Type[] { typeof(string), typeof(DynValue) } );
            var dynValueFromObject = typeof(DynValue).GetMethod("FromObject");
            var dynValueNewTable = typeof(DynValue).GetMethod("NewTable", new Type[] { typeof(Script) });
            var dynValueGetTable = typeof(DynValue).GetProperty("Table").GetGetMethod();

            var table = gen.DeclareLocal(typeof(Table));
            var metaTable = gen.DeclareLocal(typeof(Table));

            //                                             STACK
            // Create table
            gen.Emit(OpCodes.Ldarg_0);                  // this
            gen.Emit(OpCodes.Ldsfld, scriptField);      // this, this.script
            gen.Emit(OpCodes.Call, dynValueNewTable);   // this, <new Table>
            gen.Emit(OpCodes.Stfld, tableField);        //

            // Create metatable
            gen.Emit(OpCodes.Ldarg_0);                   //  this
            gen.Emit(OpCodes.Ldfld, tableField);         //  this.table_dv
            gen.Emit(OpCodes.Call, dynValueGetTable);    //  this.table
            gen.Emit(OpCodes.Ldsfld, scriptField);       //  this.table, this.script
            gen.Emit(OpCodes.Newobj, tableConstructor);  //  this.table, metatable
            gen.Emit(OpCodes.Stloc, metaTable);          //  this.table
            gen.Emit(OpCodes.Ldloc, metaTable);          //  this.table, metatable
            gen.Emit(OpCodes.Call, metaTableSet);        //

            // Set metatable.instance
            gen.Emit(OpCodes.Ldloc, metaTable);          // metatable
            gen.Emit(OpCodes.Ldstr, "instance");         // metatable, "instance"
            gen.Emit(OpCodes.Ldsfld, scriptField);       // metatable, "instance", this.script
            gen.Emit(OpCodes.Ldarg_0);                   // metatable, "instance", this.script, this
            gen.Emit(OpCodes.Call, dynValueFromObject);  // metatable, "instance", dynvalue<this>
            gen.Emit(OpCodes.Call, tableSet);            //

            // Set metatable.__index
            gen.Emit(OpCodes.Ldloc, metaTable);          // metatable
            gen.Emit(OpCodes.Ldstr, "__index");          // metatable, "__index"
            gen.Emit(OpCodes.Ldsfld, indexField);        // metatable, "__index", index_dv
            gen.Emit(OpCodes.Call, tableSet);            //

            // Set metatable.__newindex
            gen.Emit(OpCodes.Ldloc, metaTable);          // metatable
            gen.Emit(OpCodes.Ldstr, "__newindex");       // metatable, "__newindex"
            gen.Emit(OpCodes.Ldsfld, newIndexField);     // metatable, "__newindex", newindex_dv
            gen.Emit(OpCodes.Call, tableSet);            //
        }
    }


}
