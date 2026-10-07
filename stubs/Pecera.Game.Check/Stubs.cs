// STUBS PARA COMPROBAR TIPOS EN CI. NO SON EL JUEGO NI SUS DLL.
//
// Declaran solo las firmas que usa src/Pecera.Game. Origen de cada una:
//   - BepInEx / HarmonyLib / UnityEngine: API publica y documentada de esas librerias.
//   - Pawn, Character, FeelingReason...: las que ya compilaron contra el Assembly-CSharp.dll
//     real en la v0.1 del plugin (legacy/noble-fates-0.1) y las del informe §7 (LEIDO).
// Si el juego cambia una firma, esto seguira compilando aqui y FALLARA en el PC del usuario:
// el compilar.ps1 real es la unica comprobacion definitiva.
using System;
using System.Collections.Generic;
using System.Reflection;

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class BepInPlugin : Attribute { public BepInPlugin(string guid, string name, string version) { } }

    public sealed class PluginInfo { public string Location { get { return ""; } } }

    namespace Logging
    {
        public sealed class ManualLogSource
        {
            public void LogInfo(object o) { }
            public void LogWarning(object o) { }
            public void LogError(object o) { }
        }
    }

    public abstract class BaseUnityPlugin : UnityEngine.MonoBehaviour
    {
        public PluginInfo Info { get { return null; } }
        public Logging.ManualLogSource Logger { get { return null; } }
    }
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type t, string metodo) { }
    }

    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll() { }
        public IEnumerable<MethodBase> GetPatchedMethods() { return null; }
    }
}

namespace UnityEngine
{
    public class Object { public static void DontDestroyOnLoad(Object o) { } public HideFlags hideFlags; }
    public class Component : Object { }
    public class MonoBehaviour : Component { }
    public class GameObject : Object
    {
        public GameObject(string n) { }
        public T AddComponent<T>() where T : Component { return null; }
    }
    [Flags] public enum HideFlags { None = 0, HideAndDontSave = 61 }
    public enum KeyCode { F6 = 280, F7 = 281, F8 = 282, F9 = 283, F10 = 284, F11 = 285 }
    public static class Input { public static bool GetKeyDown(KeyCode k) { return false; } }
    public static class Debug { public static void Log(object o) { } }
    public struct Vector3
    {
        public float x, y, z;
        public static Vector3 up { get { return default(Vector3); } }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float f) { return a; }
        public static Vector3 operator *(float f, Vector3 a) { return a; }
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return default(Color); } }
    }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } }
    public sealed class RectOffset { public RectOffset(int a, int b, int c, int d) { } }
    public enum TextAnchor { UpperLeft, MiddleCenter }
    public enum FontStyle { Normal, Bold }
    public enum TextureFormat { RGBA32 }
    public sealed class GUIStyleState { public Color textColor; public Texture2D background; }
    public sealed class GUIContent { public static GUIContent none = null; public GUIContent(string s) { } }
    public sealed class GUIStyle
    {
        public GUIStyle(GUIStyle o) { }
        public GUIStyleState normal;
        public int fontSize; public bool wordWrap, richText; public TextAnchor alignment; public RectOffset padding; public FontStyle fontStyle;
        public float CalcHeight(GUIContent c, float w) { return 0; }
    }
    public sealed class GUISkin { public GUIStyle label; public GUIStyle box; }
    public static class GUI
    {
        public static GUISkin skin { get { return null; } }
        public static Color color;
        public static void Label(Rect r, string s, GUIStyle st) { }
        public static void Label(Rect r, GUIContent c, GUIStyle st) { }
        public static void Box(Rect r, GUIContent c) { }
    }
    public sealed class Texture2D : Object
    {
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public void SetPixel(int x, int y, Color c) { }
        public void Apply() { }
    }
    public sealed class Camera { public static Camera main { get { return null; } } public Vector3 WorldToScreenPoint(Vector3 v) { return v; } }
    public static class Screen { public static int height { get { return 0; } } }
    public static class Time
    {
        public static float unscaledDeltaTime { get { return 0; } }
        public static float unscaledTime { get { return 0; } }
        public static float smoothDeltaTime { get { return 0; } }
    }
    public static class Mathf
    {
        public static int RoundToInt(float f) { return 0; }
        public static int Clamp(int v, int a, int b) { return v; }
    }
    public static class Application { public static string version { get { return ""; } } public static string unityVersion { get { return ""; } } }
}

// ---- tipos del juego (global namespace, como en Assembly-CSharp.dll) ----
public interface ISubject { }
public interface ISubjectOrCompound { }
public struct FeelingReason { }
public enum FeelingMemoryFlags { None, Reminder }
public class Actor { public UnityEngine.Vector3 pos; }
public class Character : Actor { }
public class Pawn : ISubject, ISubjectOrCompound
{
    public Character character { get { return null; } }
    public string GetName() { return ""; }
    public string GetShortName() { return ""; }
    public string GetFullName() { return ""; }
    public string GetLogName() { return ""; }
    public float GetOpinionValue(ISubjectOrCompound s, bool b) { return 0; }
    public void DeltaOpinionOfSubject(ISubject s, float delta, FeelingReason r, FeelingMemoryFlags f) { }
}
public class PawnManager { public void OpinionDelta(Pawn p, ISubject of, float delta, FeelingReason reason) { } }
