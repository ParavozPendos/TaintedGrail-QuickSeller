// Runtime compatibility: the same sources build for both game runtimes —
// IL2CPP (default Steam/GOG build, BepInEx 6) and Mono (the "mono" beta branch, BepInEx 5).
#if IL2CPP
global using ObjId = System.IntPtr;
global using GType = Il2CppSystem.Type;
global using GAction = Il2CppSystem.Action;
#else
global using GType = System.Type;
global using GAction = System.Action;
#endif
using System;
using UnityEngine;
#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
#endif

#if !IL2CPP
// Identity of a game object as a dictionary key (the IL2CPP build uses the native pointer).
internal readonly struct ObjId : IEquatable<ObjId>
{
    private readonly object _o;
    internal ObjId(object o) { _o = o; }
    public bool Equals(ObjId other) => ReferenceEquals(_o, other._o);
    public override bool Equals(object obj) => obj is ObjId id && Equals(id);
    public override int GetHashCode() => _o == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_o);
    public static bool operator ==(ObjId a, ObjId b) => a.Equals(b);
    public static bool operator !=(ObjId a, ObjId b) => !a.Equals(b);
}
#endif

internal static class Compat
{
#if IL2CPP
    internal const string Runtime = "IL2CPP";
    internal static readonly ObjId NoId = new IntPtr(-1);
    internal static ObjId Id(this Il2CppObjectBase o) => o == null ? IntPtr.Zero : o.Pointer;
    internal static bool Same(Il2CppObjectBase a, Il2CppObjectBase b) => a != null && b != null && a.Pointer == b.Pointer;
    // Cast through the object itself (also works from an interface wrapper to the class).
    internal static T As<T>(Il2CppObjectBase o) where T : Il2CppObjectBase => o == null ? null : new Il2CppSystem.Object(o.Pointer).TryCast<T>();
    internal static GType TypeOf<T>() => Il2CppType.Of<T>();
    internal static GType GameType(this Il2CppSystem.Object o) => o?.GetIl2CppType();
    internal static GAction Action(Action a) => DelegateSupport.ConvertDelegate<GAction>(a);
    internal static bool Dead(UnityEngine.Object o) => o == null || o.WasCollected;
#else
    internal const string Runtime = "Mono";
    internal static readonly ObjId NoId = new ObjId(new object());
    internal static ObjId Id(this object o) => new ObjId(o);
    internal static bool Same(object a, object b) => a != null && ReferenceEquals(a, b);
    internal static T As<T>(object o) where T : class => o as T;
    internal static T TryCast<T>(this object o) where T : class => o as T;
    internal static GType TypeOf<T>() => typeof(T);
    internal static GType GameType(this object o) => o?.GetType();
    internal static GAction Action(Action a) => a;
    internal static bool Dead(UnityEngine.Object o) => o == null;

    // BepInEx 5 has no AddComponent on the plugin: our behaviours live on their own hidden, persistent object.
    internal static T Spawn<T>(string name) where T : Component
    {
        var go = new GameObject(name);
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.hideFlags = HideFlags.HideAndDontSave;
        return go.AddComponent<T>();
    }
#endif
}
