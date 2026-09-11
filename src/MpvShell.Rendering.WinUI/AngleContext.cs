using System.Runtime.InteropServices;
using MpvShell.Rendering.WinUI.Interop;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MpvShell.Rendering.WinUI;

/// <summary>
/// 渲染线程独占的 EGLDisplay/context；直接将同一 D3D11 设备的后备纹理用作 GL 默认帧缓冲。
/// 依据 ANGLE 的 device_creation_d3d11 和 d3d_texture_client_buffer 扩展。
/// </summary>
internal sealed unsafe class AngleContext : IDisposable
{
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private nint _device;
    private nint _display;
    private nint _context;
    private nint _parkingSurface;
    private nint _textureSurface;
    private ID3D11Texture2D? _texture;
    private bool _disposed;

    public AngleContext(nint d3d11Device, Format format = Format.B8G8R8A8_UNorm)
    {
        try
        {
            RequireExtension(0, "EGL_ANGLE_device_creation");
            RequireExtension(0, "EGL_ANGLE_device_creation_d3d11");
            RequireExtension(0, "EGL_EXT_platform_device");
            _device = AngleNative.CreateDevice(AngleNative.D3D11DeviceAngle, d3d11Device, null);
            Require(_device != 0, "eglCreateDeviceANGLE");
            _display = AngleNative.GetPlatformDisplay(AngleNative.PlatformDeviceExt, _device, null);
            Require(_display != 0, "eglGetPlatformDisplayEXT");
            Require(AngleNative.Initialize(_display, out _, out _) != 0, "eglInitialize");
            RequireExtension(_display, "EGL_ANGLE_d3d_texture_client_buffer");
            RequireExtension(_display, "EGL_KHR_no_config_context");

            Require(AngleNative.QueryDisplayAttribute(_display, AngleNative.DeviceExt, out var displayDevice) != 0,
                "eglQueryDisplayAttribEXT");
            Require(AngleNative.QueryDeviceAttribute(displayDevice, AngleNative.D3D11DeviceAngle, out var nativeDevice) != 0,
                "eglQueryDeviceAttribEXT");
            if (nativeDevice != d3d11Device)
                throw new InvalidOperationException("ANGLE 与 Composition SwapChain 没有使用同一 D3D11 设备。");

            var parkingConfig = ChooseConfig(Format.B8G8R8A8_UNorm);
            SetFormat(format);
            Require(AngleNative.BindApi(0x30A0) != 0, "eglBindAPI(OpenGL ES)");
            int* contextAttributes = stackalloc int[] { 0x3098, 3, AngleNative.None };
            // 无 config context 可以绑定不同位深的 EGL surface，切换 SDR/PQ10 时
            // 保留 libmpv 的 GL 资源、解码器与当前暂停帧；锁定 ANGLE D3D11 支持该扩展。
            _context = AngleNative.CreateContext(_display, 0, 0, contextAttributes);
            Require(_context != 0, "eglCreateContext(ES3)");
            int* surfaceAttributes = stackalloc int[] { 0x3057, 1, 0x3056, 1, AngleNative.None };
            _parkingSurface = AngleNative.CreatePbufferSurface(_display, parkingConfig, surfaceAttributes);
            Require(_parkingSurface != 0, "eglCreatePbufferSurface");
            MakeParkingCurrent();
            Description = Marshal.PtrToStringUTF8(AngleNative.GlGetString(0x1F01)) ?? "ANGLE D3D11";
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public string Description { get; } = string.Empty;
    public int GlInternalFormat { get; private set; }
    public int ColorDepth { get; private set; }

    public nint GetProcAddress(string name)
    {
        VerifyThread();
        return AngleNative.GetProcAddress(name);
    }

    /// <summary>取得纹理引用的所有权；ResizeBuffers 前必须先释放此引用及 EGL surface。</summary>
    public void ImportBackBuffer(ID3D11Texture2D texture)
    {
        VerifyThread();
        ArgumentNullException.ThrowIfNull(texture);
        try
        {
            var format = texture.Description.Format;
            var config = ChooseConfig(format);
            ReleaseBackBuffer();
            _texture = texture;
            int* attributes = stackalloc int[] { AngleNative.None };
            // D3D texture 的实际格式定义 EGL storage。不要在非TYPELESS纹理上
            // 指定 EGL_GL_COLORSPACE；PQ 编码由 mpv 负责、DXGI ColorSpace 由宿主负责。
            _textureSurface = AngleNative.CreatePbufferFromClientBuffer(
                _display, AngleNative.D3DTextureAngle, texture.NativePointer, config, attributes);
            Require(_textureSurface != 0, $"eglCreatePbufferFromClientBuffer({format})");
            MakeBackBufferCurrent();
            SetFormat(format);
        }
        catch
        {
            // 本方法接管传入引用，包括格式协商失败路径。
            if (!ReferenceEquals(_texture, texture)) texture.Dispose();
            throw;
        }
    }

    public void MakeBackBufferCurrent()
    {
        VerifyThread();
        Require(_textureSurface != 0, "视频帧缓冲未创建");
        MakeCurrent(_textureSurface);
    }

    public void MakeParkingCurrent()
    {
        VerifyThread();
        MakeCurrent(_parkingSurface);
    }

    public void ClearBlack()
        => ClearColor(0, 0, 0, 1);

    internal void ClearColor(float red, float green, float blue, float alpha)
    {
        MakeBackBufferCurrent();
        AngleNative.BindFramebuffer(0x8D40, 0);
        float* values = stackalloc float[] { red, green, blue, alpha };
        AngleNative.ClearBuffer(0x1800, 0, values); // GL_COLOR；保留浮点目标超过1/负值。
    }

    /// <summary>
    /// 扩展规定纹理作为 current draw/read surface 时 D3D 内容未定义。
    /// 先提交 GL 命令并切至 parking surface，再由同一设备执行 DXGI Present。
    /// </summary>
    public void PreparePresent()
    {
        VerifyThread();
        AngleNative.Flush();
        MakeParkingCurrent();
    }

    public void ReleaseBackBuffer()
    {
        VerifyThread();
        if (_textureSurface != 0)
        {
            MakeParkingCurrent();
            // 通过 GL 实际绑定 parking RTV，解除 immediate context 对旧 backbuffer 的引用。
            // 不能直接 D3D ClearState，否则 ANGLE 的状态缓存会与设备失配。
            AngleNative.BindFramebuffer(0x8D40, 0);
            AngleNative.Clear(0x00004000);
            AngleNative.Flush();
            var surface = _textureSurface;
            _textureSurface = 0;
            Require(AngleNative.DestroySurface(_display, surface) != 0, "eglDestroySurface");
        }
        _texture?.Dispose();
        _texture = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        VerifyThread();
        _disposed = true;
        if (_display != 0)
        {
            // 设备丢失时清理必须继续，不能因第一个 EGL 错误跳过其余原生资源。
            AngleNative.MakeCurrent(_display, 0, 0, 0);
            if (_textureSurface != 0) AngleNative.DestroySurface(_display, _textureSurface);
            if (_parkingSurface != 0) AngleNative.DestroySurface(_display, _parkingSurface);
            if (_context != 0) AngleNative.DestroyContext(_display, _context);
            AngleNative.Terminate(_display);
        }
        _texture?.Dispose();
        _texture = null;
        if (_device != 0) AngleNative.ReleaseDevice(_device);
        AngleNative.ReleaseThread();
        _device = _display = _context = _parkingSurface = _textureSurface = 0;
    }

    private void MakeCurrent(nint surface) => Require(
        AngleNative.MakeCurrent(_display, surface, surface, _context) != 0, "eglMakeCurrent");

    private nint ChooseConfig(Format format)
    {
        var (bits, alpha, component, _) = DescribeFormat(format);
        RequireExtension(_display, "EGL_EXT_pixel_format_float");
        int* attributes = stackalloc int[]
        {
            0x3033, 0x0001, // EGL_SURFACE_TYPE, EGL_PBUFFER_BIT
            0x3024, bits, 0x3023, bits, 0x3022, bits, 0x3021, alpha,
            0x3040, 0x0040, // EGL_RENDERABLE_TYPE, EGL_OPENGL_ES3_BIT
            0x3025, 0, 0x3026, 0, 0x3031, 0, // 无 depth/stencil/MSAA
            AngleNative.ColorComponentType, component,
            AngleNative.None,
        };
        Require(AngleNative.ChooseConfig(_display, attributes, out var config, 1, out var count) != 0 && count == 1,
            $"eglChooseConfig({format})");
        // eglChooseConfig 位数条件是下限；禁止接受高位深请求被错误的 config 满足。
        foreach (var (attribute, expected) in new[] { (0x3024, bits), (0x3023, bits), (0x3022, bits), (0x3021, alpha),
            (AngleNative.ColorComponentType, component) })
        {
            Require(AngleNative.GetConfigAttribute(_display, config, attribute, out var value) != 0,
                "eglGetConfigAttrib");
            if (value != expected)
                throw new NotSupportedException($"ANGLE 无法提供精确的 {format} 帧缓冲配置。");
        }
        return config;
    }

    private void SetFormat(Format format)
    {
        var description = DescribeFormat(format);
        ColorDepth = description.Bits;
        GlInternalFormat = description.GlFormat;
    }

    private static (int Bits, int Alpha, int Component, int GlFormat) DescribeFormat(Format format) => format switch
    {
        Format.B8G8R8A8_UNorm or Format.R8G8B8A8_UNorm => (8, 8, AngleNative.FixedComponent, 0x8058), // GL_RGBA8
        Format.R10G10B10A2_UNorm => (10, 2, AngleNative.FixedComponent, 0x8059), // GL_RGB10_A2
        Format.R16G16B16A16_Float => (16, 16, AngleNative.FloatComponent, 0x881A), // GL_RGBA16F
        _ => throw new NotSupportedException($"尚未定义 {format} 的 EGL/GL 输出格式。"),
    };

    private static void RequireExtension(nint display, string extension)
    {
        var extensions = Marshal.PtrToStringUTF8(AngleNative.QueryString(display, AngleNative.Extensions)) ?? string.Empty;
        if (!extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(extension, StringComparer.Ordinal))
            throw new NotSupportedException($"当前 ANGLE 不支持必要扩展：{extension}。");
    }

    private void VerifyThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("EGL 上下文只能在创建它的渲染线程使用。");
    }

    private static void Require(bool result, string operation)
    {
        if (!result)
            throw new InvalidOperationException($"{operation} 失败，EGL 错误 0x{AngleNative.GetError():X4}。");
    }
}
