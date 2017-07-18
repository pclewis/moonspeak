# MoonSpeak

MoonSpeak is a RimWorld mod for writing RimWorld mods in Lua.

## Why?

You can have an in-game REPL and reload your code without restarting.

## Is it fast?

Doubt it!

## Usage

Under your Mod folder, create `Defs/MoonSpeakInit.xml`

With contents (fill in mod name):
```.xml
<MoonSpeak>
<MoonSpeak.InitializeModule Module="{{MODNAME}}"/>
</MoonSpeak>
```

Then create `Lua/main.lua` that sets up your mod.

Make sure the MoonSpeak mod is in the load order before your mod.

## Loading C# classes

Use `require`:

```.lua
local Find = require('Verse.Find')

Find.MapUI.selector.SingleSelectedThing.Kill()
```

To instantiate, use `__new`:

```.lua
local Rect = require('UnityEngine.Rect')

local myRect = Rect.__new(1,2,3,4)
```

For generics, you need to use the [mangled name](https://msdn.microsoft.com/en-us/library/w3f99sx1(v=vs.110).aspx#code-snippet-4):

```.cs
// C#
var allThings = Verse.DefDatabase<Verse.ThingDef>.AllDefs
```
```.lua
-- Lua
local allThings = require('Verse.DefDatabase`1[Verse.ThingDef]').AllDefs
```

Lua's `type` will return `userdata` for C# objects. Use `typeof` to get their type.

## Defining C# classes

Use `class(name, baseType, functionTable)`:

```.lua
function MainTabWindow_MySweetMod:DoWindowContents(inRect)
  GUI.Label( inRect, 'whoa' )
end

class( "LuaTest.MainTabWindow_MySweetMod", typeof(require('RimWorld.MainTabWindow')), MainTabWindow_MySweetMod )
```

Any method names that match virtual methods on the parent class will be
implemented as overrides.

`self` in the Lua functions is a table that has a metatable pointing both to the
functionTable provided and the backing C# object. This should mostly just work
the way you would expect.

If you re-define a class, the function table on all new and existing instances
is updated.

## How does it work

MoonSpeak is powered by [MoonSharp](https://github.com/xanathar/moonsharp/).

## License

MoonSpeak is free and unencumbered software released into the public domain. See
`UNLICENSE`.

MoonSharp is Copyright (c) 2014-2016, Marco Mastropaolo. See license and
copyright notice in `MoonSharp.LICENSE`.
