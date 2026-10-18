using Lucent.Mac.Interop;
using SharpMetal.Foundation;
using SharpMetal.Metal;

namespace Lucent.Mac.Visuals;

/// <summary>How a pipeline mixes what it draws with what's already there.</summary>
public enum MetalBlend
{
    /// <summary>Replaces it.</summary>
    None,

    /// <summary>Adds to it at full strength (Lucent's particles: glowing dots on top of each other).</summary>
    Add,

    /// <summary>Over it, by its alpha (MilkDrop's shapes and waves).</summary>
    Alpha,

    /// <summary>Adds to it, by its alpha (MilkDrop's additive shapes and waves).</summary>
    AddAlpha,
}

/// <summary>Compiling Metal source and making render pipelines from it, for the visuals and MilkDrop presets alike.</summary>
public static class MetalPipelines
{
    /// <summary>Compiles Metal source and returns one of its functions. The library is kept in <paramref name="libraries"/> to be freed later.</summary>
    public static MTLFunction LoadFunction(MTLDevice device, string source, string name, List<MTLLibrary> libraries)
    {
        var options = new MTLCompileOptions
        {
            LanguageVersion = MTLLanguageVersion.Version21,
            // Strict maths, like the HLSL compiler: the particle shaders check isfinite() to respawn
            // particles whose numbers went bad, which fast maths is allowed to assume never happens.
            FastMathEnabled = false,
        };
        var error = new NSError(IntPtr.Zero);
        var library = device.NewLibrary(StringHelper.Create(source), options, ref error);
        options.Dispose();
        if (library.NativePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException(Describe(error, "The Metal shader didn't compile."));
        }

        lock (libraries)
        {
            libraries.Add(library);
        }

        var function = library.NewFunction(StringHelper.Create(name));
        if (function.NativePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"The Metal shader has no {name} function.");
        }

        return function;
    }

    public static MTLRenderPipelineState Make(MTLDevice device, MTLFunction vertex, MTLFunction fragment, MTLPixelFormat format, MetalBlend blend)
    {
        var description = new MTLRenderPipelineDescriptor
        {
            VertexFunction = vertex,
            FragmentFunction = fragment,
        };
        var colour = description.ColorAttachments.Object(0);
        colour.PixelFormat = format;
        if (blend != MetalBlend.None)
        {
            var source = blend == MetalBlend.Add ? MTLBlendFactor.One : MTLBlendFactor.SourceAlpha;
            var destination = blend == MetalBlend.Alpha ? MTLBlendFactor.OneMinusSourceAlpha : MTLBlendFactor.One;
            ObjC.SendVoid(colour.NativePtr, "setBlendingEnabled:", true);
            colour.RgbBlendOperation = MTLBlendOperation.Add;
            colour.AlphaBlendOperation = MTLBlendOperation.Add;
            colour.SourceRGBBlendFactor = source;
            colour.DestinationRGBBlendFactor = destination;
            colour.SourceAlphaBlendFactor = source;
            colour.DestinationAlphaBlendFactor = destination;
        }

        var error = new NSError(IntPtr.Zero);
        var pipeline = device.NewRenderPipelineState(description, ref error);
        description.Dispose();
        if (pipeline.NativePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException(Describe(error, "A Metal pipeline couldn't be made."));
        }

        return pipeline;
    }

    public static string Describe(NSError error, string fallback) =>
        error.NativePtr != IntPtr.Zero ? StringHelper.Read(error.LocalizedDescription) ?? fallback : fallback;
}
