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
        public static int ReturnOne() { return 1; }
    }

    public abstract class OverrideMe
    {
        public int n = 0;
        public virtual int ReturnOne() { return 1; }
        public virtual int SimpleMethod() { return 1; }
        public virtual string MethodWithParams(int i, string s) { return "hi"; }
        public virtual int IntWithRef(ref int i) { return i; }
        public virtual void VoidWithRef(ref int i) { i = 1; }
        public virtual int IntWithRefAndOut(ref int i, out string s) { s = i.ToString(); return i; }
    }

    public abstract class Abstract
    {
        public abstract int ReturnOne();
    }

    [TestFixture]
    public class TestClass
    {
        const bool DUMP_ASSEMBLY = false;
        Script script;
        System.Reflection.Emit.AssemblyBuilder assembly;

        static TestClass()
        {
            UserData.RegistrationPolicy = MoonSharp.Interpreter.Interop.InteropRegistrationPolicy.Automatic;
        }

        [SetUp]
        public void SetUpScript()
        {
            assembly = TypeMaker.MakeAssembly("test");
            var module = TypeMaker.MakeModule(assembly, "test", "test.dll");
            script = new Script();
            script.Options.ScriptLoader = new TypeResolvingScriptLoader();
            script.Globals.RegisterModuleType<MoonSpeakLuaFunctions>();
            script.Globals["_moonSpeakModule"] = module;
        }

        [TearDown]
        public void TearDown()
        {
            if (DUMP_ASSEMBLY) {
                assembly.Save("test.dll");
            }
        }

        [Test]
        public void TestTypeLoader()
        {
            Assert.AreEqual(1, script.DoString("return require('Tests.LoadMe').ReturnOne()").ToObject<int>());
        }

        [Test]
        public void TestDefineClassSimpleMethod()
        {
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {SimpleMethod=|| 90210} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual(90210, instance.SimpleMethod());
        }

        [Test]
        public void TestDefineClassMethodWithParams()
        {
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {MethodWithParams=|self,i,s|  tostring(i) .. s} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual("313ET", instance.MethodWithParams(313, "ET"));
        }

        [Test]
        public void TestDefineClassIntWithRef()
        {
            // note: metalua lambda syntax doesn't support tuple return
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {IntWithRef=function(self, i) return i, i*i end} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 256;
            Assert.AreEqual(256, instance.IntWithRef(ref i));
            Assert.AreEqual(256 * 256, i);
        }

        [Test]
        public void TestDefineClassVoidWithRef()
        {
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
            Console.WriteLine(script.Globals["class"]);
            Console.WriteLine(script.Globals["typeof"]);
            DynValue wrappedType = script.DoString("return class( 'Overridden', typeof(require('Tests.OverrideMe')), {IntWithRefAndOut=function(self,i,s) return i+i, i*i, tostring(i) end} )");

            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            int i = 150;
            Assert.AreEqual(150 + 150, instance.IntWithRefAndOut(ref i, out string s));
            Assert.AreEqual(150 * 150, i);
            Assert.AreEqual("150", s);
        }

        [Test]
        public void TestInstanceVars()
        {
            DynValue wrappedType = script.DoString(@"return class( 'Overridden', typeof(require('Tests.OverrideMe')), {
                ReturnTwo=||2,
                SimpleMethod=function(self)
                              self.x = 8675309
                              self.n = 15
                              return self.x+self.ReturnOne()+self.ReturnTwo()+self.n
                end} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual(8675309+1+2+15, instance.SimpleMethod());
            Assert.AreEqual(15, instance.n);
        }

        [Test]
        public void TestNoNewTableEntryOnInvalidTypeForField()
        {
            DynValue wrappedType = script.DoString(@"return class( 'Overridden', typeof(require('Tests.OverrideMe')), {
                SimpleMethod=function(self)
                              self.n = 'hi'
                              return self.n
                end} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.Throws<MoonSharp.Interpreter.ScriptRuntimeException>( delegate { instance.SimpleMethod(); });
            Assert.True(((DynValue)type.GetField("_moonSpeakTable").GetValue(instance)).Table.Get('n').IsNil() );
        }

        [Test]
        public void TestReloadWithNewMethodOverridden()
        {
            DynValue wrappedType = script.DoString(@"return class( 'Overridden', typeof(require('Tests.OverrideMe')), {SimpleMethod=||100} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (OverrideMe)Activator.CreateInstance(type);
            Assert.AreEqual(100, instance.SimpleMethod());
            Assert.AreEqual(1, instance.ReturnOne());
            DynValue wrappedType2 = script.DoString(@"return class( 'Overridden', typeof(require('Tests.OverrideMe')), {ReturnOne=||200} )");
            Type type2 = wrappedType.ToObject<Type>();
            Assert.AreEqual(type, type2);
            Assert.AreEqual(1, instance.SimpleMethod());
            Assert.AreEqual(200, instance.ReturnOne());
        }

        [Test]
        public void TestCustom()
        {
            DynValue wrappedType = script.DoString(@"return class('HelloWorld', typeof(require('System.Object')),
              {Greet = |self,n| 'Hello, ' .. n},
              function(typeBuilder)
                typeBuilder.addInstanceMethod('Greet', typeof(require('System.String')), { typeof(require('System.String')) }, 'Greet' );
              end)");
            var type = wrappedType.ToObject<Type>();
            var instance = Activator.CreateInstance(type);
            var result = (String)type.GetMethod("Greet").Invoke(instance, new object[] { "world" });
            Assert.AreEqual("Hello, world", result);
        }

        [Test]
        public void TestCustomStatic()
        {
            DynValue wrappedType = script.DoString(@"return class('HelloWorld', typeof(require('System.Object')),
              {Greet = |n| 'Hello, ' .. n},
              function(typeBuilder)
                typeBuilder.addStaticMethod('Greet', typeof(require('System.String')), { typeof(require('System.String')) }, 'Greet' );
              end)");
            var type = wrappedType.ToObject<Type>();
            var result = (String)type.GetMethod("Greet").Invoke(null, new object[] { "world" });
            Assert.AreEqual("Hello, world", result);
        }

        [Test]
        public void TestMeta()
        {
            Assert.AreEqual(3, script.DoString(@"local t = require('Tests.LoadMe')
              local parent = {ReturnTwo = ||2}
              local self = {}
              setmetatable(self, {__index=parent})
              setmetatable(parent, {__index=t})
              return self.ReturnOne() + self.ReturnTwo()").ToObject<int>());
        }

        [Test]
        public void TestMetaMeta()
        {
            DynValue wrappedType = script.DoString(@"
              local ftabparent = {Greet = |n| 'Hello, ' .. n}
              local ftab = {}
              setmetatable(ftab, {__index=ftabparent})
              return class('TestMetaMeta.HelloWorld', typeof(require('System.Object')),
                ftab,
                function(typeBuilder)
                  typeBuilder.addStaticMethod('Greet', typeof(require('System.String')), { typeof(require('System.String')) }, 'Greet' );
                end)");
            var type = wrappedType.ToObject<Type>();
            var result = (String)type.GetMethod("Greet").Invoke(null, new object[] { "world" });
            Assert.AreEqual("Hello, world", result);
        }

        [Test]
        public void TestAbstractBase()
        {
            DynValue wrappedType = script.DoString("return class( 'Abstracted', typeof(require('Tests.Abstract')), {ReturnOne=|| 11111} )");
            Type type = wrappedType.ToObject<Type>();
            var instance = (Abstract)Activator.CreateInstance(type);
            Assert.AreEqual(11111, instance.ReturnOne());
        }
    }
}
