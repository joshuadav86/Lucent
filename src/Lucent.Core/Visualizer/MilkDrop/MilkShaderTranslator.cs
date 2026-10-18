using System.Text;
using System.Text.RegularExpressions;

namespace WinampInspiredVisualizer.Visualizer.MilkDrop;

/// <summary>Which of a MilkDrop 2 preset's two shaders: the warp (feeds back into the next frame) or the composite (what's shown).</summary>
public enum MilkShaderKind
{
    Warp,
    Composite,
}

/// <summary>A MilkDrop 2 shader made into Shader Model 5 HLSL, and what it reads.</summary>
/// <param name="Source">The whole shader, prelude included, ready to compile.</param>
/// <param name="EntryPoint">The pixel shader function in it.</param>
/// <param name="BlurLevels">How many of the blur textures (0-3) it reads, so only those are made each frame.</param>
/// <param name="StandIns">Pictures it wanted from MilkDrop's textures folder, which it gets noise for instead.</param>
public sealed record MilkTranslatedShader(string Source, string EntryPoint, int BlurLevels, IReadOnlyList<string> StandIns);

/// <summary>
/// Turns a MilkDrop 2 warp or composite shader (DirectX 9 HLSL, written as a "shader_body { ... }"
/// that sets <c>ret</c>) into a Shader Model 5 pixel shader for Lucent's MilkDrop prelude
/// (Shaders/MilkDrop.hlsli). DirectX 9's samplers become a texture and a sampler state each
/// (sampler_fw_main is the main image, filtered and wrapping; sampler_pc_noise_lq the low-quality
/// noise, point-sampled and clamped...), tex2D and tex3D become functions taking both, and the
/// preset's own declarations of samplers and texsize_ values are dropped since the prelude
/// provides them. Textures a preset loads from image files aren't available, so they're given
/// one of the noise textures instead.
/// </summary>
public static class MilkShaderTranslator
{
    public const string WarpEntryPoint = "PSWarpPreset";
    public const string CompositeEntryPoint = "PSCompPreset";

    private static readonly Regex SamplerName = new(@"\bsampler_(\w+)", RegexOptions.Compiled);
    private static readonly Regex Sampler3D = new(@"\btex3[dD]\s*\(\s*sampler_(\w+)", RegexOptions.Compiled);
    private static readonly Regex SamplerDeclaration = new(
        @"\b(?:uniform\s+)?(?:sampler|sampler2D|sampler3D|samplerCUBE|texture)\s+sampler_\w+\s*(?::\s*register\s*\([^)]*\))?\s*;",
        RegexOptions.Compiled);
    private static readonly Regex TexsizeDeclaration = new(@"\b(?:uniform\s+)?float4\s+texsize_\w+\s*(?::\s*register\s*\([^)]*\))?\s*;", RegexOptions.Compiled);
    private static readonly Regex TexsizeName = new(@"\btexsize_(\w+)", RegexOptions.Compiled);
    private static readonly Regex Blur = new(@"(?:GetBlur|sampler_(?:[fp][wc]_)?blur)([123])", RegexOptions.Compiled);
    private static readonly Regex ShaderBody = new(@"\bshader_body\b", RegexOptions.Compiled);

    // Words Shader Model 5 keeps for itself that a DirectX 9 shader could use as names.
    private static readonly Regex Reserved = new(
        @"(?<![\.\w])(sample|point|line|triangle|lineadj|triangleadj|precise|unorm|snorm|groupshared|export|interface|class|namespace|globallycoherent|nointerpolation|noperspective)\b",
        RegexOptions.Compiled);

    // MilkDrop's shaders were compiled for Shader Model 2 and 3, which have no doubles: a double there
    // is a float. Shader Model 5 would make it a real double, and Metal (on the Mac) has none at all.
    private static readonly Regex Double = new(@"\bdouble([1-4](?:x[1-4])?)?\b", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Replacement)[] Functions =
    {
        (new Regex(@"\btex2[dD]lod\s*\(", RegexOptions.Compiled), "md_tex2Dlod("),
        (new Regex(@"\btex2[dD]bias\s*\(", RegexOptions.Compiled), "md_tex2Dbias("),
        (new Regex(@"\btex2[dD]proj\s*\(", RegexOptions.Compiled), "md_tex2Dproj("),
        (new Regex(@"\btex2[dD]\s*\(", RegexOptions.Compiled), "md_tex2D("),
        (new Regex(@"\btex3[dD]\s*\(", RegexOptions.Compiled), "md_tex3D("),
    };

    /// <summary>
    /// Makes the shader's whole source from the prelude (Shaders/MilkDrop.hlsli) and the preset's
    /// shader text. Throws <see cref="FormatException"/> if the text has no shader_body.
    /// </summary>
    public static MilkTranslatedShader Translate(string prelude, string code, MilkShaderKind kind)
    {
        var body = ShaderBody.Match(code);
        if (!body.Success)
        {
            throw new FormatException("The shader has no shader_body.");
        }

        var blurLevels = Blur.Matches(code).Select(match => match.Groups[1].Value[0] - '0').DefaultIfEmpty(0).Max();
        var threeD = Sampler3D.Matches(code).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
        var samplers = SamplerName.Matches(code).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();
        var texsizes = TexsizeName.Matches(code).Select(match => match.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

        var globals = Clean(code[..body.Index]);
        var main = Clean(code[(body.Index + body.Length)..]).Trim();
        if (!main.StartsWith('{'))
        {
            main = "{\n" + main + "\n}";
        }

        var source = new StringBuilder(prelude.Length + code.Length + 2048);
        source.Append(prelude).Append('\n');
        var standIns = new List<string>();
        foreach (var name in samplers)
        {
            var (texture, state, known) = SamplerFor(name, threeD.Contains(name));
            if (!known)
            {
                standIns.Add(name);
            }

            source.Append("#define sampler_").Append(name).Append(' ').Append(texture).Append(", ").Append(state).Append('\n');
        }

        foreach (var name in texsizes)
        {
            source.Append("#define texsize_").Append(name).Append(' ').Append(TexsizeFor(name)).Append('\n');
        }

        source.Append("#line 1\n").Append(globals).Append('\n');
        var entryPoint = kind == MilkShaderKind.Warp ? WarpEntryPoint : CompositeEntryPoint;
        if (kind == MilkShaderKind.Warp)
        {
            source.Append("float4 ").Append(entryPoint).Append("(MdWarpVertex _md_in) : SV_Target\n{\n")
                .Append("    float2 uv = _md_in.uv.xy;\n")
                .Append("    float2 uv_orig = _md_in.uv.zw;\n")
                .Append("    float rad = _md_in.radAng.x;\n")
                .Append("    float ang = _md_in.radAng.y;\n");
        }
        else
        {
            source.Append("float4 ").Append(entryPoint).Append("(MdScreenVertex _md_in) : SV_Target\n{\n")
                .Append("    float2 uv = _md_in.uv;\n")
                .Append("    float2 uv_orig = _md_in.uv;\n")
                .Append("    float rad = MdRad(uv);\n")
                .Append("    float ang = MdAng(uv);\n");
        }

        source.Append("    float3 hue_shader = MdHueShader(uv_orig);\n")
            .Append("    float3 ret = 0;\n")
            .Append(main).Append('\n')
            .Append("    return float4(saturate(ret), 1.0);\n}\n");
        return new MilkTranslatedShader(source.ToString(), entryPoint, blurLevels, standIns);
    }

    private static string Clean(string code)
    {
        code = SamplerDeclaration.Replace(code, string.Empty);
        code = TexsizeDeclaration.Replace(code, string.Empty);
        code = Double.Replace(code, "float$1");
        foreach (var (pattern, replacement) in Functions)
        {
            code = pattern.Replace(code, replacement);
        }

        return Reserved.Replace(code, "md_$1");
    }

    /// <summary>The prelude's texture and sampler state for a MilkDrop sampler name such as "fc_main" or "noise_lq".</summary>
    private static (string Texture, string State, bool Known) SamplerFor(string name, bool threeD)
    {
        string? state = null;
        var texture = name;
        if (name.Length > 3 && name[2] == '_' && (name[0] is 'f' or 'p') && (name[1] is 'w' or 'c'))
        {
            state = (name[0], name[1]) switch
            {
                ('f', 'w') => "MdLinearWrap",
                ('f', 'c') => "MdLinearClamp",
                ('p', 'w') => "MdPointWrap",
                _ => "MdPointClamp",
            };
            texture = name[3..];
        }

        var (resource, defaultState, known) = texture switch
        {
            "main" => ("MdMain", "MdLinearWrap", true),
            "blur1" => ("MdBlur1", "MdLinearClamp", true),
            "blur2" => ("MdBlur2", "MdLinearClamp", true),
            "blur3" => ("MdBlur3", "MdLinearClamp", true),
            "noise_lq" => ("MdNoiseLq", "MdLinearWrap", true),
            "noise_lq_lite" => ("MdNoiseLqLite", "MdLinearWrap", true),
            "noise_mq" => ("MdNoiseMq", "MdLinearWrap", true),
            "noise_hq" => ("MdNoiseHq", "MdLinearWrap", true),
            "noisevol_lq" => ("MdNoiseVolLq", "MdLinearWrap", true),
            "noisevol_hq" => ("MdNoiseVolHq", "MdLinearWrap", true),

            // An image the preset expected to load from MilkDrop's textures folder.
            _ => (threeD ? "MdNoiseVolHq" : "MdNoiseMq", "MdLinearWrap", false),
        };
        return (resource, state ?? defaultState, known);
    }

    /// <summary>A texture's size as MilkDrop gives it: width, height, 1/width, 1/height.</summary>
    private static string TexsizeFor(string name) => name switch
    {
        "main" => "texsize",
        "blur1" => "float4(texsize.xy * 0.5, texsize.zw * 2.0)",
        "blur2" => "float4(texsize.xy * 0.25, texsize.zw * 4.0)",
        "blur3" => "float4(texsize.xy * 0.125, texsize.zw * 8.0)",
        "noise_lq_lite" or "noisevol_lq" or "noisevol_hq" => "float4(32.0, 32.0, 1.0 / 32.0, 1.0 / 32.0)",
        _ => "float4(256.0, 256.0, 1.0 / 256.0, 1.0 / 256.0)",
    };
}
