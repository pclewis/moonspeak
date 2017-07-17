﻿using MoonSharp.Interpreter;
using NUnit.Framework;
using MoonSpeak;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tests
{
    public static class LoadMe
    {
        public static int ReturnOne() { return 1; }
    }

    public abstract class OverrideMe
    {
        public virtual int SimpleMethod() { return 1; }
        public virtual string MethodWithParams(int i, string s) { return "hi"; }
        public virtual int IntWithRef(ref int i) { return i;  }
        public virtual void VoidWithRef(ref int i) { i = 1; }
        public virtual int IntWithRefAndOut(ref int i, out string s) { s = i.ToString(); return i; }
    }

    [TestFixture]
    public class TestClass
    {
        static TestClass()
        {
            UserData.RegistrationPolicy = MoonSharp.Interpreter.Interop.InteropRegistrationPolicy.Automatic;
        }

        [Test]
        public void TestTypeLoader()
        {
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            Assert.AreEqual(1, script.DoString("return require('Tests.LoadMe').ReturnOne()").ToObject<int>());
        }

        [Test]
        public void TestDefineClassSimpleMethod()
        {
            var module = TypeMaker.MakeModule("testDefineClassSimpleMethod");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.MakeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {SimpleMethod=|| 90210} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual(90210, instance.SimpleMethod());
        }

        [Test]
        public void TestDefineClassMethodWithParams()
        {
            var module = TypeMaker.MakeModule("testDefineClassMethodWithParams");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.MakeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {MethodWithParams=|self,i,s|  tostring(i) .. s} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual("313ET", instance.MethodWithParams(313, "ET"));
        }

        [Test]
        public void TestDefineClassIntWithRef()
        {
            var module = TypeMaker.MakeModule("testDefineClassIntWithRef");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.MakeType(script, module, baseType, name, delegates));
            // note: metalua lambda syntax doesn't support tuple return
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {IntWithRef=function(self, i) return i, i*i end} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 256;
            Assert.AreEqual(256, instance.IntWithRef(ref i));
            Assert.AreEqual(256*256, i);
        }

        [Test]
        public void TestDefineClassVoidWithRef()
        {
            var module = TypeMaker.MakeModule("testDefineClassVoidWithRef");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.MakeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {VoidWithRef=|self,i| i*i} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 256;
            instance.VoidWithRef(ref i);
            Assert.AreEqual(256 * 256, i);
        }

        [Test]
        public void TestDefineClassIntWithRefAndOut()
        {
            var module = TypeMaker.MakeModule("testDefineClassIntWithRefAndOut");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.MakeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {IntWithRefAndOut=function(self,i,s) return i+i, i*i, tostring(i) end} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 150;
            Assert.AreEqual(150 + 150, instance.IntWithRefAndOut(ref i, out string s));
            Assert.AreEqual(150*150, i);
            Assert.AreEqual("150", s);
        }

        [Test]
        public void TestInstanceVars()
        {
            var assembly = TypeMaker.MakeAssembly("test");
            var module = TypeMaker.MakeModule(assembly, "testInstanceVars", "test.dll");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.MakeType(script, module, baseType, name, delegates));
            script.Options.DebugPrint = (s => Console.WriteLine(s));
            Console.WriteLine("hi");
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {SimpleMethod=function(self) print(type(self)); self.x = 8675309; return self.x end} )");

            Type type = wrappedType.ToObject<Type>();
            assembly.Save("test.dll");
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual(8675309, instance.SimpleMethod());

        }

        [Test]
        public void TestMeta()
        {
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            Assert.AreEqual(3, script.DoString(@"local t = require('Tests.LoadMe')
              local parent = {ReturnTwo = ||2}
              local self = {}
              setmetatable(self, {__index=parent})
              setmetatable(parent, {__index=t})
              return self.ReturnOne() + self.ReturnTwo()").ToObject<int>());
        }
    }
}
