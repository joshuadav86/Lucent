using System.Runtime.InteropServices;
using System.Text;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Microsoft's DXC shader compiler, loaded from its library (libdxcompiler.dylib, which ships beside the
/// app), for turning HLSL into SPIR-V while the app runs: the first half of translating the user's own
/// visuals to Metal. It's the same compiler the build uses through its command line tool.
/// </summary>
/// <remarks>
/// DXC's API is COM-style: objects are pointers to tables of functions, laid out the same on every system
/// since DXC 1.7 (its IUnknown outside Windows no longer has a virtual destructor). Strings it takes are
/// wchar_t, four bytes a character outside Windows.
/// </remarks>
public static unsafe class DxcCompiler
{
    private const uint Utf8CodePage = 65001;

    private static readonly Guid CompilerClass = new("73e22d93-e6ce-47f3-b5bf-f0664f39c1b0");
    private static readonly Guid CompilerInterface = new("228b4687-5a6a-4730-900c-9702b2203f54");
    private static readonly Guid ResultInterface = new("58346cda-dde7-4497-9461-6f87af5e0659");

    private static readonly object Gate = new();
    private static readonly Lazy<IntPtr> CreateInstance = new(LoadLibrary);

    // Every interface starts with IUnknown's QueryInterface, AddRef and Release.
    private const int FirstMethod = 3;

    /// <summary>The library's file name on this system.</summary>
    public static string LibraryName =>
        OperatingSystem.IsWindows() ? "dxcompiler.dll" : OperatingSystem.IsMacOS() ? "libdxcompiler.dylib" : "libdxcompiler.so";

    /// <summary>True when the compiler's library can be found (it's loaded the first time it's used).</summary>
    public static bool IsAvailable => FindLibrary() is not null;

    /// <summary>Compiles assembled HLSL to SPIR-V with the Mac app's options. Throws with DXC's message on errors.</summary>
    public static byte[] ToSpirv(string hlsl, string entryPoint, string sourceName)
    {
        var create = (delegate* unmanaged<Guid*, Guid*, IntPtr*, int>)CreateInstance.Value;
        var arguments = new List<string> { sourceName };
        arguments.AddRange(MetalTranslation.DxcArguments(entryPoint));

        lock (Gate)
        {
            IntPtr compiler = IntPtr.Zero;
            IntPtr result = IntPtr.Zero;
            IntPtr output = IntPtr.Zero;
            IntPtr errors = IntPtr.Zero;
            var strings = new List<IntPtr>();
            try
            {
                var clsid = CompilerClass;
                var iid = CompilerInterface;
                Check(create(&clsid, &iid, &compiler), "start the shader compiler");

                foreach (var argument in arguments)
                {
                    strings.Add(WideString(argument));
                }

                var source = Encoding.UTF8.GetBytes(hlsl);
                var argumentPointers = strings.ToArray();
                var resultIid = ResultInterface;
                fixed (byte* text = source)
                fixed (IntPtr* args = argumentPointers)
                {
                    var buffer = new DxcBuffer { Pointer = (IntPtr)text, Size = (nuint)source.Length, Encoding = Utf8CodePage };
                    // IDxcCompiler3::Compile(source, arguments, count, include handler, riid, result)
                    var compile = (delegate* unmanaged<IntPtr, DxcBuffer*, IntPtr*, uint, IntPtr, Guid*, IntPtr*, int>)Method(compiler, 0);
                    Check(compile(compiler, &buffer, args, (uint)argumentPointers.Length, IntPtr.Zero, &resultIid, &result), "compile the shader");
                }

                // IDxcOperationResult::GetStatus, GetResult, GetErrorBuffer
                long status = 0;
                var getStatus = (delegate* unmanaged<IntPtr, long*, int>)Method(result, 0);
                Check(getStatus(result, &status), "read the compiler's result");
                if ((int)status < 0)
                {
                    var getErrors = (delegate* unmanaged<IntPtr, IntPtr*, int>)Method(result, 2);
                    var message = getErrors(result, &errors) >= 0 && errors != IntPtr.Zero ? BlobText(errors) : null;
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
                        ? $"The shader didn't compile (0x{(int)status:X8})."
                        : message.Trim());
                }

                var getResult = (delegate* unmanaged<IntPtr, IntPtr*, int>)Method(result, 1);
                Check(getResult(result, &output), "read the compiled shader");
                return BlobBytes(output);
            }
            finally
            {
                foreach (var pointer in strings)
                {
                    Marshal.FreeHGlobal(pointer);
                }

                Release(errors);
                Release(output);
                Release(result);
                Release(compiler);
            }
        }
    }

    /// <summary>Where DXC's library is: $LUCENT_DXC_LIBRARY, beside the app, or beside $DXC (the command line tool's own lib folder).</summary>
    private static string? FindLibrary()
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable("LUCENT_DXC_LIBRARY") is { Length: > 0 } named)
        {
            candidates.Add(named);
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, LibraryName));
        if (Environment.GetEnvironmentVariable("DXC") is { Length: > 0 } tool && Path.GetDirectoryName(tool) is { } bin)
        {
            candidates.Add(Path.Combine(bin, "..", "lib", LibraryName));
            candidates.Add(Path.Combine(bin, LibraryName));
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    private static IntPtr LoadLibrary()
    {
        var path = FindLibrary() ?? throw new DllNotFoundException(
            $"Lucent's shader compiler ({LibraryName}) isn't beside the app, so your own visuals can't be translated for this Mac.");
        var library = NativeLibrary.Load(path);
        return NativeLibrary.GetExport(library, "DxcCreateInstance");
    }

    /// <summary>The address of an interface's method, counting from the first one after IUnknown's.</summary>
    private static IntPtr Method(IntPtr instance, int index)
    {
        var table = *(IntPtr**)instance;
        return table[FirstMethod + index];
    }

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            var release = (delegate* unmanaged<IntPtr, uint>)(*(IntPtr**)instance)[2];
            release(instance);
        }
    }

    /// <summary>IDxcBlob's bytes: GetBufferPointer and GetBufferSize.</summary>
    private static byte[] BlobBytes(IntPtr blob)
    {
        var pointer = ((delegate* unmanaged<IntPtr, IntPtr>)Method(blob, 0))(blob);
        var size = ((delegate* unmanaged<IntPtr, nuint>)Method(blob, 1))(blob);
        var bytes = new byte[(int)size];
        Marshal.Copy(pointer, bytes, 0, bytes.Length);
        return bytes;
    }

    private static string BlobText(IntPtr blob) => Encoding.UTF8.GetString(BlobBytes(blob)).TrimEnd('\0');

    /// <summary>A wchar_t string: two bytes a character on Windows, four elsewhere.</summary>
    private static IntPtr WideString(string text)
    {
        if (OperatingSystem.IsWindows())
        {
            return Marshal.StringToHGlobalUni(text);
        }

        var bytes = Encoding.UTF32.GetBytes(text + "\0");
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    private static void Check(int result, string doing)
    {
        if (result < 0)
        {
            throw new InvalidOperationException($"Couldn't {doing} (0x{result:X8}).");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DxcBuffer
    {
        public IntPtr Pointer;
        public nuint Size;
        public uint Encoding;
    }
}
