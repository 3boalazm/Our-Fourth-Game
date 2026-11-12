using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// C# side of NetBridge.jslib (the WebSocket room relay, see /relay).
/// In the Unity Editor and non-WebGL builds there is no network: everything is a stub.
/// </summary>
public static class Net
{
    public enum Status { Idle = 0, Connecting = 1, InRoom = 2, Error = 6, Taken = 7, NotFound = 8, Reconnecting = 9 }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void NB_Start(int isHost, string code);
    [DllImport("__Internal")] static extern int NB_Status();
    [DllImport("__Internal")] static extern string NB_Error();
    [DllImport("__Internal")] static extern int NB_Slot();
    [DllImport("__Internal")] static extern void NB_Send(string msg);
    [DllImport("__Internal")] static extern string NB_Poll();
    [DllImport("__Internal")] static extern void NB_Close();
    [DllImport("__Internal")] static extern string NB_UrlParam(string name);
    [DllImport("__Internal")] static extern string NB_PageUrl();
    [DllImport("__Internal")] static extern int NB_Share(string text, string url);
    [DllImport("__Internal")] static extern int NB_CopyText(string text);
    [DllImport("__Internal")] static extern void NB_Vibrate(int ms);
    [DllImport("__Internal")] static extern void NB_Report(string json);
    [DllImport("__Internal")] static extern string NB_GetStored(string key);
    [DllImport("__Internal")] static extern void NB_SetStored(string key, string value);
    [DllImport("__Internal")] static extern void NB_AskName(string current);

    public static void Start(bool host, string code) { NB_Start(host ? 1 : 0, code); }
    public static Status Current { get { return (Status)NB_Status(); } }
    public static string Error { get { return NB_Error(); } }
    public static int Slot { get { return NB_Slot(); } }
    public static void Send(string msg) { NB_Send(msg); }
    public static string Poll() { string s = NB_Poll(); return string.IsNullOrEmpty(s) ? null : s; }
    public static void Close() { NB_Close(); }
    public static string UrlParam(string name) { return NB_UrlParam(name); }
    public static string PageUrl() { return NB_PageUrl(); }
    public static bool Share(string text, string url) { return NB_Share(text, url) == 1; }
    public static bool CopyText(string text) { return NB_CopyText(text) == 1; }
    public static void Vibrate(int ms) { NB_Vibrate(ms); }
    public static void Report(string json) { NB_Report(json); }
    public static string GetStored(string key) { return NB_GetStored(key); }
    public static void SetStored(string key, string value) { NB_SetStored(key, value); }
    public static void AskName(string current) { NB_AskName(current); }
    public static bool Available { get { return true; } }
#else
    static string askedName;
    public static void Start(bool host, string code) { }
    public static Status Current { get { return Status.Error; } }
    public static string Error { get { return "online play needs the web build"; } }
    public static int Slot { get { return -1; } }
    public static void Send(string msg) { }
    public static string Poll()
    {
        if (askedName == null) return null;
        string s = "N|" + askedName;
        askedName = null;
        return s;
    }
    public static void Close() { }
    public static string UrlParam(string name) { return ""; }
    public static string PageUrl() { return "http://localhost/"; }
    public static bool Share(string text, string url) { return false; }
    public static bool CopyText(string text) { GUIUtility.systemCopyBuffer = text; return true; }
    public static void Vibrate(int ms) { }
    public static void Report(string json) { }
    public static string GetStored(string key) { return PlayerPrefs.GetString("ocg_" + key, ""); }
    public static void SetStored(string key, string value) { PlayerPrefs.SetString("ocg_" + key, value); PlayerPrefs.Save(); }
    public static void AskName(string current) { askedName = current; }
    public static bool Available { get { return false; } }
#endif
}
