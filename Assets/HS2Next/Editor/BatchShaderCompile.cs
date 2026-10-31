// BatchShaderCompile: a test process whose command line holds -syncShaders compiles shader variants before their first draw.
// The Editor compiles a newly used shader variant in the background and draws a placeholder (the error colour) until it is
// ready, so the first frame of a clothed character built after other drawing could show it. Render and capture rows pass
// -syncShaders (their method arguments); the switch is set once when the Editor loads, in batch mode with a graphics device
// only. Timing rows and interactive Editor sessions keep the background compile.
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class BatchShaderCompile
{
    public const string Flag = "-syncShaders";

    static BatchShaderCompile()
    {
        if (!Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        if (Array.IndexOf(Environment.GetCommandLineArgs(), Flag) < 0) return;
        ShaderUtil.allowAsyncCompilation = false;
        Debug.Log("[BatchShaderCompile] -syncShaders: shader variants compile before their first draw (ShaderUtil.allowAsyncCompilation = false)");
    }
}
