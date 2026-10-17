using System.Diagnostics;
using System.Runtime.InteropServices;
using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Cross;

namespace Lucent.MetalShaders;

/// <summary>
/// Turns one HLSL entry point into Metal Shading Language, with its resources at the slots the Mac
/// renderer binds them to (see <see cref="Slots"/>) and the entry point renamed to a fixed name.
/// </summary>
public static unsafe class ShaderTranslator
{
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

    private static readonly Cross Api = Cross.GetApi();

    public static string ToMetal(string hlsl, string entryPoint, string sourceName)
    {
        var (profile, model) = entryPoint switch
        {
            "CSMain" => ("cs_6_0", ExecutionModel.GLCompute),
            "VSMain" or "VSParticle" => ("vs_6_0", ExecutionModel.Vertex),
            _ => ("ps_6_0", ExecutionModel.Fragment),
        };

        var spirv = ToSpirv(hlsl, entryPoint, profile, sourceName);
        return SpirvToMetal(spirv, entryPoint, model);
    }

    /// <summary>The DXC command line compiler: $DXC, or dxc on the PATH.</summary>
    public static string Dxc { get; set; } = Environment.GetEnvironmentVariable("DXC") is { Length: > 0 } dxc ? dxc : "dxc";

    private static byte[] ToSpirv(string hlsl, string entryPoint, string profile, string sourceName)
    {
        var folder = Directory.CreateTempSubdirectory("lucent-metal-");
        try
        {
            var input = Path.Combine(folder.FullName, sourceName);
            var output = Path.Combine(folder.FullName, "shader.spv");
            File.WriteAllText(input, hlsl);
            var start = new ProcessStartInfo(Dxc)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[]
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
                "-Fo", output,
                input,
            })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(errors.Result.Trim());
            }

            return File.ReadAllBytes(output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static string SpirvToMetal(byte[] spirv, string entryPoint, ExecutionModel model)
    {
        Context* context = null;
        Api.ContextCreate(&context).Check(context, "create a SPIRV-Cross context");
        try
        {
            ParsedIr* ir = null;
            var words = MemoryMarshal.Cast<byte, uint>(spirv);
            fixed (uint* code = words)
            {
                Api.ContextParseSpirv(context, code, (nuint)words.Length, &ir).Check(context, "read the SPIR-V");
            }

            Compiler* compiler = null;
            Api.ContextCreateCompiler(context, Backend.Msl, ir, CaptureMode.TakeOwnership, &compiler).Check(context, "start the Metal compiler");

            CompilerOptions* options = null;
            Api.CompilerCreateCompilerOptions(compiler, &options).Check(context, "read the compiler options");
            // Metal 2.1 (macOS 10.14): everything the shaders use, on every Mac that runs Metal 2.
            Api.CompilerOptionsSetUint(options, CompilerOption.MslVersion, 20100).Check(context, "set the Metal version");
            Api.CompilerOptionsSetUint(options, CompilerOption.MslPlatform, 1).Check(context, "target macOS");
            Api.CompilerInstallCompilerOptions(compiler, options).Check(context, "apply the compiler options");

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
                Api.CompilerMslAddResourceBinding(compiler, &binding).Check(context, "place the shader's resources");
            }

            Api.CompilerRenameEntryPoint(compiler, entryPoint, MetalNames[entryPoint], model).Check(context, "name the entry point");

            byte* source = null;
            Api.CompilerCompile(compiler, &source).Check(context, "write Metal");
            return Marshal.PtrToStringUTF8((IntPtr)source)!;
        }
        finally
        {
            Api.ContextDestroy(context);
        }
    }

    private static void Check(this Result result, Context* context, string doing)
    {
        if (result != Result.Success)
        {
            throw new InvalidOperationException($"Couldn't {doing}: {Api.ContextGetLastErrorStringS(context)}");
        }
    }
}
