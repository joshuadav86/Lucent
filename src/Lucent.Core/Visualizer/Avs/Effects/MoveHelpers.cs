using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>What the movement effects (Movement, Dynamic Movement, Dynamic Shift, Dynamic Distance Modifier) share.</summary>
internal static class MoveHelpers
{
    /// <summary>
    /// Compiles code as <see cref="AvsEffect.Compile"/> does, and says whether AVS would have had code to run:
    /// its compiler gave nothing back for code that was empty (only spaces, semicolons, or comments) or
    /// didn't compile, and several effects behave differently then.
    /// </summary>
    public static EelCode Compile(AvsEffect effect, EelContext context, string source, string label, out bool hasCode)
    {
        var before = effect.Warnings.Count;
        var code = effect.Compile(context, source, label);
        var failed = false;
        for (var i = before; i < effect.Warnings.Count; i++)
        {
            failed |= effect.Warnings[i].StartsWith(label + " didn't compile", StringComparison.Ordinal);
        }

        hasCode = !failed && HasStatements(source);
        return code;
    }

    /// <summary>Whether the code has anything in it besides spaces, semicolons, and comments.</summary>
    public static bool HasStatements(string source)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                while (i < source.Length && source[i] != '\n')
                {
                    i++;
                }
            }
            else if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                i = end < 0 ? source.Length : end + 1;
            }
            else if (c > ' ' && c != ';')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <see cref="AvsBlend.Bilinear"/> (BLEND4) without the array bounds checks, for the per-pixel loops where it
    /// is most of the work. The callers keep <paramref name="at"/> + <paramref name="width"/> + 1 inside the picture.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Bilinear(int[] p, int at, int width, int xp, int yp)
    {
        ref var table = ref MemoryMarshal.GetArrayDataReference(AvsBlend.Table);
        ref var pixel = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(p), at);
        var a1 = Unsafe.Add(ref table, ((255 - xp) << 8) | (255 - yp));
        var a2 = Unsafe.Add(ref table, (xp << 8) | (255 - yp));
        var a3 = Unsafe.Add(ref table, ((255 - xp) << 8) | yp);
        var a4 = Unsafe.Add(ref table, (xp << 8) | yp);
        var c1 = pixel;
        var c2 = Unsafe.Add(ref pixel, 1);
        var c3 = Unsafe.Add(ref pixel, width);
        var c4 = Unsafe.Add(ref pixel, width + 1);
        var r = Unsafe.Add(ref table, ((c1 & 0xff) << 8) | a1) + Unsafe.Add(ref table, ((c2 & 0xff) << 8) | a2)
              + Unsafe.Add(ref table, ((c3 & 0xff) << 8) | a3) + Unsafe.Add(ref table, ((c4 & 0xff) << 8) | a4);
        var g = Unsafe.Add(ref table, (c1 & 0xff00) | a1) + Unsafe.Add(ref table, (c2 & 0xff00) | a2)
              + Unsafe.Add(ref table, (c3 & 0xff00) | a3) + Unsafe.Add(ref table, (c4 & 0xff00) | a4);
        var b = Unsafe.Add(ref table, ((c1 >> 8) & 0xff00) | a1) + Unsafe.Add(ref table, ((c2 >> 8) & 0xff00) | a2)
              + Unsafe.Add(ref table, ((c3 >> 8) & 0xff00) | a3) + Unsafe.Add(ref table, ((c4 >> 8) & 0xff00) | a4);
        return Math.Min(r, 255) | (Math.Min(g, 255) << 8) | (Math.Min(b, 255) << 16);
    }
}
