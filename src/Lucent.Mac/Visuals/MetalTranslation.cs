using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Silk.NET.Core.Contexts;
using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Cross;

namespace Lucent.Mac.Visuals;

/// <summary>
/// How the visuals' HLSL becomes Metal Shading Language: HLSL to SPIR-V with Microsoft's DXC compiler,
/// then SPIR-V to Metal with Khronos's SPIRV-Cross, with each resource at the slot the Mac renderer
/// binds it to and each entry point renamed to a fixed name. The same rules serve the build
/// (tools/MetalShaders, which links this file and runs the dxc command line tool) and the app, which
/// translates the user's own visuals (and edited built-in ones) as it runs (see <see cref="DxcCompiler"/>).
/// </summary>
public static unsafe class MetalTranslation
{
    /// <summary>Bump when the translation changes, so earlier results (shipped or cached) aren't reused.</summary>
    public const string Version = "msl21-pow-1";

    /// <summary>The first line of a translated file: "// Lucent source &lt;hash&gt;", the hash of the HLSL it was made from.</summary>
    public const string SourceLinePrefix = "// Lucent source ";

    /// <summary>The Metal function name each HLSL entry point becomes.</summary>
    public static readonly IReadOnlyDictionary<string, string> MetalNames = new Dictionary<string, string>
    {
        ["VSMain"] = "engine_vertex",
        ["PSPresent"] = "engine_present",
        ["PSBlend"] = "engine_blend",
        ["PSDownsample"] = "engine_downsample",
        ["PSSprite"] = "sprite_fragment",
        ["PSMain"] = "preset_fragment",
        ["CSMain"] = "particles_simulate",
        ["VSParticle"] = "particles_vertex",
    };

    // DXC gives each kind of HLSL register its own range of SPIR-V bindings, so they can't collide:
    // b# at 0, t# at 10, s# at 20 and u# at 30.
    private const int TextureShift = 10;
    private const int SamplerShift = 20;
    private const int UavShift = 30;

    /// <summary>
    /// Where each HLSL register lands in Metal. Textures keep their t# number; the constant buffer
    /// and the particle buffers become Metal buffers.
    /// </summary>
    private static readonly (uint Binding, uint Buffer, uint Texture, uint Sampler)[] Slots =
    {
        (0, 0, 0, 0),                    // b0: Params                 -> buffer(0)
        (TextureShift + 0, 0, 0, 0),     // t0: PreviousFrame          -> texture(0)
        (TextureShift + 1, 0, 1, 0),     // t1: AudioData              -> texture(1)
        (TextureShift + 2, 0, 2, 0),     // t2: NextFrame              -> texture(2)
        (TextureShift + 3, 0, 3, 0),     // t3: ParticleLayer          -> texture(3)
        (TextureShift + 4, 1, 4, 0),     // t4: ParticlesToDraw        -> buffer(1)
        (SamplerShift + 0, 0, 0, 0),     // s0: LinearClamp            -> sampler(0)
        (UavShift + 0, 2, 0, 0),         // u0: ParticleState          -> buffer(2)
    };

    /// <summary>
    /// Put in front of every shader before it's translated. The Windows app compiles with fxc, which
    /// turns pow(x, 2.0), pow(x, 3.0) and so on into plain multiplications, so a negative x squares
    /// as you'd expect. Through SPIR-V, pow becomes Metal's powr, which is only defined for x &gt;= 0
    /// and gives NaN for the rest: whole pictures went black (Ripple Lattice, Meteor Shower's stars,
    /// Neural Bloom's firing). This pow gives whole-number powers of negative numbers their proper
    /// value, and leaves everything else to the real pow, as on Windows.
    /// </summary>
    public const string Prelude = """
        #define LUCENT_POW(T) \
        T lucentPow(T x, T y) \
        { \
            T magnitude = pow(abs(x), y); \
            T sign = abs(fmod(y, 2.0)) == 1.0 ? -1.0 : 1.0; \
            return (x < 0.0 && y == round(y)) ? magnitude * sign : pow(x, y); \
        }
        LUCENT_POW(float)
        LUCENT_POW(float2)
        LUCENT_POW(float3)
        LUCENT_POW(float4)
        float2 lucentPow(float2 x, float y) { return lucentPow(x, float2(y, y)); }
        float3 lucentPow(float3 x, float y) { return lucentPow(x, float3(y, y, y)); }
        float4 lucentPow(float4 x, float y) { return lucentPow(x, float4(y, y, y, y)); }
        float2 lucentPow(float x, float2 y) { return lucentPow(float2(x, x), y); }
        float3 lucentPow(float x, float3 y) { return lucentPow(float3(x, x, x), y); }
        float4 lucentPow(float x, float4 y) { return lucentPow(float4(x, x, x, x), y); }
        #undef LUCENT_POW
        #define pow lucentPow

        """;

    /// <summary>The shader model profile and stage for an entry point.</summary>
    public static (string Profile, ExecutionModel Model) StageOf(string entryPoint) => entryPoint switch
    {
        "CSMain" => ("cs_6_0", ExecutionModel.GLCompute),
        "VSMain" or "VSParticle" => ("vs_6_0", ExecutionModel.Vertex),
        _ => ("ps_6_0", ExecutionModel.Fragment),
    };

    /// <summary>DXC's options, apart from the input and output files.</summary>
    public static IReadOnlyList<string> DxcArguments(string entryPoint)
    {
        var (profile, _) = StageOf(entryPoint);
        return new[]
        {
            "-E", entryPoint,
            "-T", profile,
            "-spirv",
            "-fspv-target-env=vulkan1.1",
            // fxc's rules (the Windows app compiles with fxc): ?: works on vectors, picking per component.
            "-HV", "2018",
            "-O3",
            // Same constant and structured buffer layout as Direct3D, so the app fills them the same way.
            "-fvk-use-dx-layout",
            "-fvk-b-shift", "0", "0",
            "-fvk-t-shift", $"{TextureShift}", "0",
            "-fvk-s-shift", $"{SamplerShift}", "0",
            "-fvk-u-shift", $"{UavShift}", "0",
            // Not -fvk-invert-y: Metal's clip space points y up like Direct3D's, so the shaders' own maths is right as it is.
        };
    }

    /// <summary>The engine's own shaders, assembled as the Windows renderer does (Renderer.CompileEngineShaders).</summary>
    public static string EngineSource(string common, string engine) => Prelude + common + "\n" + engine;

    /// <summary>The particle sprite's shader (PSSprite), assembled as the Windows renderer does.</summary>
    public static string SpriteSource(string common, string particles) => Prelude + common + "\n" + particles;

    /// <summary>
    /// One of a visual's entry points (PSMain, CSMain or VSParticle), assembled as the Windows renderer
    /// does (Renderer.CompileLoop): the shared API, then the visual's own code, then the entry point's file.
    /// </summary>
    public static string PresetSource(string common, string particles, string presetMain, string particleMain, string code, bool isParticles, string entryPoint)
    {
        var api = isParticles ? common + "\n" + particles : common;
        var assembled = api + "\n#line 1\n" + code + "\n";
        return Prelude + assembled + (entryPoint == "PSMain" ? presetMain : particleMain);
    }

    /// <summary>A short fingerprint of a shader's assembled HLSL and how it's translated, to tell whether a translation is still current.</summary>
    public static string SourceHash(string hlsl, string entryPoint)
    {
        var bytes = Encoding.UTF8.GetBytes($"{Version}\n{entryPoint}\n{hlsl.ReplaceLineEndings("\n")}");
        return Convert.ToHexString(SHA256.HashData(bytes))[..32];
    }

    /// <summary>The hash a translated file says it was made from, or null if it doesn't say.</summary>
    public static string? ReadSourceHash(string metal)
    {
        if (!metal.StartsWith(SourceLinePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = metal.IndexOf('\n');
        return (end < 0 ? metal[SourceLinePrefix.Length..] : metal[SourceLinePrefix.Length..end]).Trim();
    }

    /// <summary>Marks translated Metal with the hash of the HLSL it came from.</summary>
    public static string WithSourceHash(string metal, string hash) => $"{SourceLinePrefix}{hash}\n{metal}";

    private static readonly Lazy<Cross> Api = new(LoadCross);

    /// <summary>
    /// Loads SPIRV-Cross: the copy beside the app (a published app), or the one NuGet put under
    /// runtimes/ (a build), naming the file outright when it's there. Silk's own search for it works
    /// with some .NET builds and not others (it failed on GitHub's Ubuntu runners).
    /// </summary>
    private static Cross LoadCross()
    {
        var (os, file) = OperatingSystem.IsWindows() ? ("win", "spirv-cross.dll")
            : OperatingSystem.IsMacOS() ? ("osx", "libspirv-cross.dylib")
            : ("linux", "libspirv-cross.so");
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        foreach (var path in new[]
        {
            Path.Combine(AppContext.BaseDirectory, file),
            Path.Combine(AppContext.BaseDirectory, "runtimes", $"{os}-{arch}", "native", file),
        })
        {
            if (File.Exists(path))
            {
                return new Cross(new DefaultNativeContext(path));
            }
        }

        return Cross.GetApi();
    }

    /// <summary>Makes sure SPIRV-Cross loads, so a problem with it shows once, with the whole error.</summary>
    public static void LoadSpirvCross() => _ = Api.Value;

    /// <summary>Turns SPIR-V from DXC into Metal Shading Language, with the entry point given its Metal name.</summary>
    public static string SpirvToMetal(byte[] spirv, string entryPoint)
    {
        var api = Api.Value;
        var (_, model) = StageOf(entryPoint);
        Context* context = null;
        Check(api, api.ContextCreate(&context), context, "create a SPIRV-Cross context");
        try
        {
            ParsedIr* ir = null;
            var words = MemoryMarshal.Cast<byte, uint>(spirv);
            fixed (uint* code = words)
            {
                Check(api, api.ContextParseSpirv(context, code, (nuint)words.Length, &ir), context, "read the SPIR-V");
            }

            Compiler* compiler = null;
            Check(api, api.ContextCreateCompiler(context, Backend.Msl, ir, CaptureMode.TakeOwnership, &compiler), context, "start the Metal compiler");

            CompilerOptions* options = null;
            Check(api, api.CompilerCreateCompilerOptions(compiler, &options), context, "read the compiler options");
            // Metal 2.1 (macOS 10.14): everything the shaders use, on every Mac that runs Metal 2.
            Check(api, api.CompilerOptionsSetUint(options, CompilerOption.MslVersion, 20100), context, "set the Metal version");
            Check(api, api.CompilerOptionsSetUint(options, CompilerOption.MslPlatform, 1), context, "target macOS");
            Check(api, api.CompilerInstallCompilerOptions(compiler, options), context, "apply the compiler options");

            foreach (var slot in Slots)
            {
                var binding = new MslResourceBinding
                {
                    Stage = model,
                    DescSet = 0,
                    Binding = slot.Binding,
                    MslBuffer = slot.Buffer,
                    MslTexture = slot.Texture,
                    MslSampler = slot.Sampler,
                };
                Check(api, api.CompilerMslAddResourceBinding(compiler, &binding), context, "place the shader's resources");
            }

            Check(api, api.CompilerRenameEntryPoint(compiler, entryPoint, MetalNames[entryPoint], model), context, "name the entry point");

            byte* source = null;
            Check(api, api.CompilerCompile(compiler, &source), context, "write Metal");
            return Marshal.PtrToStringUTF8((IntPtr)source)!;
        }
        finally
        {
            api.ContextDestroy(context);
        }
    }

    private static void Check(Cross api, Result result, Context* context, string doing)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Couldn't {doing}: {api.ContextGetLastErrorStringS(context)}");
        }
    }
}
