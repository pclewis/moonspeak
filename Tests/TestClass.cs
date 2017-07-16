using MoonSharp.Interpreter;
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
        public static int returnone() { return 1; }
    }

    public abstract class OverrideMe
    {
        public virtual int simpleMethod() { return 1; }
        public virtual string methodWithParams(int i, string s) { return "hi"; }
        public virtual int intWithRef(ref int i) { return i;  }
        public virtual void voidWithRef(ref int i) { i = 1; }
        public virtual int intWithRefAndOut(ref int i, out string s) { s = i.ToString(); return i; }
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
            Assert.AreEqual(1, script.DoString("return require('Tests.LoadMe').returnone()").ToObject<int>());
        }

        [Test]
        public void TestDefineClassSimpleMethod()
        {
            var module = TypeMaker.makeModule("testDefineClassSimpleMethod");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.makeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {simpleMethod=|| 90210} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual(90210, instance.simpleMethod());
        }

        [Test]
        public void TestDefineClassMethodWithParams()
        {
            var module = TypeMaker.makeModule("testDefineClassMethodWithParams");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.makeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {methodWithParams=|self,i,s|  tostring(i) .. s} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual("313ET", instance.methodWithParams(313, "ET"));
        }

        [Test]
        public void TestDefineClassIntWithRef()
        {
            var module = TypeMaker.makeModule("testDefineClassIntWithRef");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.makeType(script, module, baseType, name, delegates));
            // note: metalua lambda syntax doesn't support tuple return
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {intWithRef=function(self, i) return i, i*i end} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 256;
            Assert.AreEqual(256, instance.intWithRef(ref i));
            Assert.AreEqual(256*256, i);
        }

        [Test]
        public void TestDefineClassVoidWithRef()
        {
            var module = TypeMaker.makeModule("testDefineClassVoidWithRef");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.makeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {voidWithRef=|self,i| i*i} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 256;
            instance.voidWithRef(ref i);
            Assert.AreEqual(256 * 256, i);
        }

        [Test]
        public void TestDefineClassIntWithRefAndOut()
        {
            var module = TypeMaker.makeModule("testDefineClassIntWithRefAndOut");
            Script script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals["typeof"] = (Func<DynValue, Type>)MoonSpeakManager.TypeOf;
            script.Globals["class"] = (Func<String, Type, Table, Type>)((name, baseType, delegates) => TypeMaker.makeType(script, module, baseType, name, delegates));
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {intWithRefAndOut=function(self,i,s) return i+i, i*i, tostring(i) end} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 150;
            string s = "uhoh";
            Assert.AreEqual(150+150, instance.intWithRefAndOut(ref i, out s));
            Assert.AreEqual(150*150, i);
            Assert.AreEqual("150", s);
        }
    }
}
