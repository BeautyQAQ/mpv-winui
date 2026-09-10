using System.Runtime.InteropServices;
using MpvShell.Player.LibMpv.Native;

namespace MpvShell.Rendering.WinUI.Interop;

/// <summary>
/// 锁定 ANGLE 736ed80 的 EGL/GLES ABI。EGLint 为 32 位，EGLAttrib 为指针宽度。
/// 扩展及签名依据该版本 include/EGL/egl.h、eglext.h、eglext_angle.h。
/// </summary>
internal static unsafe partial class AngleNative
{
    internal const int None = 0x3038;
    internal const int Extensions = 0x3055;
    internal const int DeviceExt = 0x322C;
    internal const int D3D11DeviceAngle = 0x33A1;
    internal const uint PlatformDeviceExt = 0x313F;
    internal const uint D3DTextureAngle = 0x33A3;

    static AngleNative() => NativeDependencyResolver.RegisterForAssembly(typeof(AngleNative).Assembly);

    [LibraryImport("EGL", EntryPoint = "eglCreateDeviceANGLE")]
    internal static partial nint CreateDevice(int type, nint nativeDevice, nint* attributes);
    [LibraryImport("EGL", EntryPoint = "eglReleaseDeviceANGLE")]
    internal static partial int ReleaseDevice(nint device);
    [LibraryImport("EGL", EntryPoint = "eglGetPlatformDisplayEXT")]
    internal static partial nint GetPlatformDisplay(uint platform, nint device, int* attributes);
    [LibraryImport("EGL", EntryPoint = "eglInitialize")]
    internal static partial int Initialize(nint display, out int major, out int minor);
    [LibraryImport("EGL", EntryPoint = "eglQueryString")]
    internal static partial nint QueryString(nint display, int name);
    [LibraryImport("EGL", EntryPoint = "eglQueryDisplayAttribEXT")]
    internal static partial int QueryDisplayAttribute(nint display, int attribute, out nint value);
    [LibraryImport("EGL", EntryPoint = "eglQueryDeviceAttribEXT")]
    internal static partial int QueryDeviceAttribute(nint device, int attribute, out nint value);
    [LibraryImport("EGL", EntryPoint = "eglChooseConfig")]
    internal static partial int ChooseConfig(nint display, int* attributes, out nint config, int configSize, out int count);
    [LibraryImport("EGL", EntryPoint = "eglBindAPI")]
    internal static partial int BindApi(uint api);
    [LibraryImport("EGL", EntryPoint = "eglCreateContext")]
    internal static partial nint CreateContext(nint display, nint config, nint sharedContext, int* attributes);
    [LibraryImport("EGL", EntryPoint = "eglCreatePbufferSurface")]
    internal static partial nint CreatePbufferSurface(nint display, nint config, int* attributes);
    [LibraryImport("EGL", EntryPoint = "eglCreatePbufferFromClientBuffer")]
    internal static partial nint CreatePbufferFromClientBuffer(nint display, uint type, nint texture, nint config, int* attributes);
    [LibraryImport("EGL", EntryPoint = "eglMakeCurrent")]
    internal static partial int MakeCurrent(nint display, nint draw, nint read, nint context);
    [LibraryImport("EGL", EntryPoint = "eglDestroySurface")]
    internal static partial int DestroySurface(nint display, nint surface);
    [LibraryImport("EGL", EntryPoint = "eglDestroyContext")]
    internal static partial int DestroyContext(nint display, nint context);
    [LibraryImport("EGL", EntryPoint = "eglTerminate")]
    internal static partial int Terminate(nint display);
    [LibraryImport("EGL", EntryPoint = "eglReleaseThread")]
    internal static partial int ReleaseThread();
    [LibraryImport("EGL", EntryPoint = "eglGetError")]
    internal static partial int GetError();
    [LibraryImport("EGL", EntryPoint = "eglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint GetProcAddress(string name);
    [LibraryImport("GLESv2", EntryPoint = "glFlush")]
    internal static partial void Flush();
    [LibraryImport("GLESv2", EntryPoint = "glGetString")]
    internal static partial nint GlGetString(uint name);
    [LibraryImport("GLESv2", EntryPoint = "glClearColor")]
    internal static partial void ClearColor(float red, float green, float blue, float alpha);
    [LibraryImport("GLESv2", EntryPoint = "glClear")]
    internal static partial void Clear(uint mask);
    [LibraryImport("GLESv2", EntryPoint = "glBindFramebuffer")]
    internal static partial void BindFramebuffer(uint target, uint framebuffer);
}
