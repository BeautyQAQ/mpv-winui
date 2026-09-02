[CmdletBinding()]
param(
    [string] $NativeDirectory = (Join-Path $PSScriptRoot '..\..\src\MpvShell.App\Assets\Native\win-x64')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$directory = [IO.Path]::GetFullPath($NativeDirectory)
foreach ($fileName in 'd3dcompiler_47.dll', 'libGLESv2.dll', 'libEGL.dll') {
    $path = Join-Path $directory $fileName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "缺少 ANGLE 烟测依赖：$path"
    }
}

if (-not ('AngleD3D11Smoke' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public sealed class AngleSmokeResult
{
    public int EglMajor { get; init; }
    public int EglMinor { get; init; }
    public string EglVendor { get; init; } = "";
    public string EglVersion { get; init; } = "";
    public string GlVendor { get; init; } = "";
    public string GlRenderer { get; init; } = "";
    public string GlVersion { get; init; } = "";
}

public static class AngleD3D11Smoke
{
    private const uint EGL_PLATFORM_ANGLE_ANGLE = 0x3202;
    private const int EGL_PLATFORM_ANGLE_TYPE_ANGLE = 0x3203;
    private const int EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE = 0x3208;
    private const int EGL_NONE = 0x3038;
    private const int EGL_VENDOR = 0x3053;
    private const int EGL_VERSION = 0x3054;
    private const int EGL_SURFACE_TYPE = 0x3033;
    private const int EGL_PBUFFER_BIT = 0x0001;
    private const int EGL_RED_SIZE = 0x3024;
    private const int EGL_GREEN_SIZE = 0x3023;
    private const int EGL_BLUE_SIZE = 0x3022;
    private const int EGL_ALPHA_SIZE = 0x3021;
    private const int EGL_RENDERABLE_TYPE = 0x3040;
    private const int EGL_OPENGL_ES2_BIT = 0x0004;
    private const uint EGL_OPENGL_ES_API = 0x30A0;
    private const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;
    private const int EGL_WIDTH = 0x3057;
    private const int EGL_HEIGHT = 0x3056;
    private const uint GL_VENDOR = 0x1F00;
    private const uint GL_RENDERER = 0x1F01;
    private const uint GL_VERSION = 0x1F02;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr EglGetPlatformDisplay(uint platform, IntPtr nativeDisplay, int[] attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglInitialize(IntPtr display, out int major, out int minor);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr EglQueryString(IntPtr display, int name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglChooseConfig(IntPtr display, int[] attributes, [Out] IntPtr[] configs, int configSize, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglBindApi(uint api);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr EglCreatePbufferSurface(IntPtr display, IntPtr config, int[] attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr EglCreateContext(IntPtr display, IntPtr config, IntPtr shareContext, int[] attributes);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglMakeCurrent(IntPtr display, IntPtr draw, IntPtr read, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglDestroySurface(IntPtr display, IntPtr surface);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglDestroyContext(IntPtr display, IntPtr context);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int EglTerminate(IntPtr display);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint EglGetError();
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GlGetString(uint name);

    public static AngleSmokeResult Run(string directory)
    {
        IntPtr compiler = IntPtr.Zero;
        IntPtr gles = IntPtr.Zero;
        IntPtr egl = IntPtr.Zero;
        IntPtr display = IntPtr.Zero;
        IntPtr surface = IntPtr.Zero;
        IntPtr context = IntPtr.Zero;

        try
        {
            compiler = NativeLibrary.Load(System.IO.Path.Combine(directory, "d3dcompiler_47.dll"));
            gles = NativeLibrary.Load(System.IO.Path.Combine(directory, "libGLESv2.dll"));
            egl = NativeLibrary.Load(System.IO.Path.Combine(directory, "libEGL.dll"));

            var getDisplay = Get<EglGetPlatformDisplay>(egl, "eglGetPlatformDisplayEXT");
            var initialize = Get<EglInitialize>(egl, "eglInitialize");
            var queryString = Get<EglQueryString>(egl, "eglQueryString");
            var chooseConfig = Get<EglChooseConfig>(egl, "eglChooseConfig");
            var bindApi = Get<EglBindApi>(egl, "eglBindAPI");
            var createSurface = Get<EglCreatePbufferSurface>(egl, "eglCreatePbufferSurface");
            var createContext = Get<EglCreateContext>(egl, "eglCreateContext");
            var makeCurrent = Get<EglMakeCurrent>(egl, "eglMakeCurrent");
            var destroySurface = Get<EglDestroySurface>(egl, "eglDestroySurface");
            var destroyContext = Get<EglDestroyContext>(egl, "eglDestroyContext");
            var terminate = Get<EglTerminate>(egl, "eglTerminate");
            var getError = Get<EglGetError>(egl, "eglGetError");
            var glGetString = Get<GlGetString>(gles, "glGetString");

            display = getDisplay(
                EGL_PLATFORM_ANGLE_ANGLE,
                IntPtr.Zero,
                new[] { EGL_PLATFORM_ANGLE_TYPE_ANGLE, EGL_PLATFORM_ANGLE_TYPE_D3D11_ANGLE, EGL_NONE });
            Require(display != IntPtr.Zero, "eglGetPlatformDisplay(D3D11)", getError);
            Require(initialize(display, out int major, out int minor) != 0, "eglInitialize", getError);

            var configs = new IntPtr[1];
            var configAttributes = new[] {
                EGL_SURFACE_TYPE, EGL_PBUFFER_BIT,
                EGL_RED_SIZE, 8,
                EGL_GREEN_SIZE, 8,
                EGL_BLUE_SIZE, 8,
                EGL_ALPHA_SIZE, 8,
                EGL_RENDERABLE_TYPE, EGL_OPENGL_ES2_BIT,
                EGL_NONE
            };
            Require(chooseConfig(display, configAttributes, configs, 1, out int count) != 0 && count == 1,
                "eglChooseConfig", getError);
            Require(bindApi(EGL_OPENGL_ES_API) != 0, "eglBindAPI", getError);

            surface = createSurface(display, configs[0], new[] { EGL_WIDTH, 1, EGL_HEIGHT, 1, EGL_NONE });
            Require(surface != IntPtr.Zero, "eglCreatePbufferSurface", getError);
            context = createContext(display, configs[0], IntPtr.Zero,
                new[] { EGL_CONTEXT_CLIENT_VERSION, 2, EGL_NONE });
            Require(context != IntPtr.Zero, "eglCreateContext", getError);
            Require(makeCurrent(display, surface, surface, context) != 0, "eglMakeCurrent", getError);

            var result = new AngleSmokeResult {
                EglMajor = major,
                EglMinor = minor,
                EglVendor = Text(queryString(display, EGL_VENDOR)),
                EglVersion = Text(queryString(display, EGL_VERSION)),
                GlVendor = Text(glGetString(GL_VENDOR)),
                GlRenderer = Text(glGetString(GL_RENDERER)),
                GlVersion = Text(glGetString(GL_VERSION))
            };

            if (result.GlRenderer.IndexOf("D3D11", StringComparison.OrdinalIgnoreCase) < 0 &&
                result.GlRenderer.IndexOf("Direct3D11", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidOperationException("ANGLE 未报告 D3D11 renderer：" + result.GlRenderer);
            }

            makeCurrent(display, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            destroyContext(display, context);
            context = IntPtr.Zero;
            destroySurface(display, surface);
            surface = IntPtr.Zero;
            terminate(display);
            display = IntPtr.Zero;
            return result;
        }
        finally
        {
            if (egl != IntPtr.Zero) NativeLibrary.Free(egl);
            if (gles != IntPtr.Zero) NativeLibrary.Free(gles);
            if (compiler != IntPtr.Zero) NativeLibrary.Free(compiler);
        }
    }

    private static T Get<T>(IntPtr library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private static string Text(IntPtr value) => Marshal.PtrToStringAnsi(value) ?? "";

    private static void Require(bool condition, string operation, EglGetError getError)
    {
        if (!condition)
            throw new InvalidOperationException(operation + " 失败，EGL error=0x" + getError().ToString("X4"));
    }
}
'@
}

$result = [AngleD3D11Smoke]::Run($directory)
$result | Format-List
