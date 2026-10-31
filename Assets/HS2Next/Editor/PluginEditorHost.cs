// A generic Editor entry that loads a plugin folder in edit mode and runs one static method with it loaded (checks that
// need a plugin's own code, e.g. a plugin's test assembly). The plugins load through the app's loader with an in-process
// bridge registry (no listener) and no scene; the method may build a test host and attach it (AttachApp) so that the
// plugins see its scene events. Then the plugins unload and the editor exits with the method's code.
//   Run: Tools/unity_batch.ps1 -Method PluginEditorHost.Run -NoGraphics -MethodArgs @('-pluginsDir', <dir>,
//        ['-thenAssembly', <dll>,] '-then', '<Namespace.Type.Method>', <method args...>)
// The method is static, takes no parameters or a string[] (the arguments after its name), and returns void (0), bool
// (true = 0) or int (the exit code). -thenAssembly loads one more assembly first (an Editor-side test DLL, which may
// reference the Editor assembly; it is never listed in a manifest, so players never see it). Report lines go to the log
// ("PluginEditorHost: ...") and to Spike/out/plugins/editor_host_report.txt.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HS2Next.App;
using HS2Next.App.Plugins;
using HS2Next.Content;
using UnityEditor;
using UnityEngine;

public static class PluginEditorHost
{
    /// <summary>The loader of the running entry (null outside Run).</summary>
    public static PluginHost Current { get; private set; }

    static string ReportPath => Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "plugins", "editor_host_report.txt");

    static void Line(string s)
    {
        Debug.Log("PluginEditorHost: " + s);
        try { File.AppendAllText(ReportPath, s + "\n", new UTF8Encoding(false)); } catch (Exception) { }
    }

    /// <summary>-executeMethod entry.</summary>
    public static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, "PluginEditorHost " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n", new UTF8Encoding(false));
        int code;
        try { code = RunWith(Environment.GetCommandLineArgs()); }
        catch (Exception e)
        {
            Line("RESULT: FAIL (" + e.GetType().Name + ": " + e.Message + ")\n" + e.StackTrace);
            code = 1;
        }
        Line("exit " + code);
        EditorApplication.Exit(code);
    }

    /// <summary>The entry without the exit: loads, runs the method, unloads; returns the method's code.</summary>
    public static int RunWith(string[] args)
    {
        string dir = Arg(args, "-pluginsDir"), extra = Arg(args, "-thenAssembly"), then = Arg(args, "-then");
        if (string.IsNullOrEmpty(dir)) throw new ArgumentException("-pluginsDir <dir> is required");
        if (string.IsNullOrEmpty(then)) throw new ArgumentException("-then <Namespace.Type.Method> is required");
        int ti = Array.IndexOf(args, "-then");
        var rest = args.Skip(ti + 2).ToArray();
        string userData = Path.Combine(ContentSettings.ProjectRoot, "Spike", "out", "plugins", "editor_host", "userdata");
        var host = new PluginHost(null, userData);
        host.Bridge.Start(false);
        Current = host;
        try
        {
            var launch = PluginLaunch.Resolve(dir, false, null, true, null, null, ContentSettings.ProjectRoot);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            host.Load(launch);
            Line(host.Summary + "; " + string.Join(", ", host.Entries.Select(e => e.Guid + " " + e.State + (e.Code != null ? " (" + e.Code + ")" : "") +
                (e.Assemblies.Count > 0 ? " [" + string.Join(" ", e.Assemblies.Select(a => a.Name + " " + a.Call + " " + a.LoadMs.ToString("F2") + " ms")) + "]" : ""))));
            if (!string.IsNullOrEmpty(extra))
            {
                var a = Assembly.LoadFrom(Path.GetFullPath(extra));
                Line("loaded " + a.GetName().Name + " from " + extra);
            }
            var m = FindMethod(then);
            if (m == null) throw new ArgumentException("no static method " + then + " in the loaded assemblies");
            object[] pars = m.GetParameters().Length == 0 ? null : new object[] { rest };
            object r;
            try { r = m.Invoke(null, pars); }
            catch (TargetInvocationException e) { throw e.InnerException ?? e; }
            int code = r is int i ? i : r is bool b ? (b ? 0 : 1) : 0;
            Line(then + " returned " + (r ?? "(void)") + " -> exit " + code);
            return code;
        }
        finally
        {
            host.Shutdown();
            Current = null;
        }
    }

    /// <summary>Lets the method's test host deliver its scene events to the loaded plugins (call after building it).</summary>
    public static void AttachApp(AppCore app)
    {
        if (Current == null) throw new InvalidOperationException("no plugin set is loaded");
        Current.AttachTo(app);
    }

    static MethodInfo FindMethod(string full)
    {
        int dot = full.LastIndexOf('.');
        if (dot <= 0) return null;
        string typeName = full.Substring(0, dot), method = full.Substring(dot + 1);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t;
            try { t = asm.GetType(typeName, false); }
            catch (Exception) { continue; }
            if (t == null) continue;
            var m = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == method && (x.GetParameters().Length == 0 || (x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(string[]))));
            if (m != null) return m;
        }
        return null;
    }

    static string Arg(string[] a, string name)
    {
        int i = Array.IndexOf(a, name);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
}
