namespace Lucent.Visualizer.MilkDrop;

/// <summary>
/// The variables one set of MilkDrop equations works on (the per-frame equations, the per-pixel
/// ones, or one custom wave's or shape's), plus its own megabuf. Variables are found by name when
/// code is compiled and read by index while it runs, so running needs no lookups.
/// </summary>
public sealed class EelContext
{
    private readonly Dictionary<string, int> _indices = new(StringComparer.Ordinal);
    private EelMemory? _megabuf;

    public EelContext(EelGlobals globals)
    {
        Globals = globals;
    }

    /// <summary>The registers and gmegabuf every preset shares, as in MilkDrop.</summary>
    public EelGlobals Globals { get; }

    /// <summary>Every variable's value, in the order they were first named. Grows as code is compiled.</summary>
    public double[] Values { get; private set; } = new double[32];

    public int Count => _indices.Count;

    /// <summary>
    /// Functions the host adds to the language for this context's code, by lower-case name, each a
    /// Func taking and giving doubles (AVS adds getosc, getspec and gettime). Add them before compiling.
    /// </summary>
    public Dictionary<string, Delegate> Functions { get; } = new(StringComparer.Ordinal);

    /// <summary>This context's megabuf, made the first time some code uses it.</summary>
    public EelMemory Megabuf => _megabuf ??= new EelMemory();

    /// <summary>The index of a variable (names are lower case), adding it if it's new.</summary>
    public int Index(string name)
    {
        if (_indices.TryGetValue(name, out var index))
        {
            return index;
        }

        index = _indices.Count;
        _indices[name] = index;
        if (index >= Values.Length)
        {
            var grown = new double[Values.Length * 2];
            Values.CopyTo(grown, 0);
            Values = grown;
        }

        return index;
    }

    public bool Has(string name) => _indices.ContainsKey(name);

    public double this[string name]
    {
        get => Values[Index(name)];
        set => Values[Index(name)] = value;
    }
}

/// <summary>What all running MilkDrop presets share: reg00 to reg99 and gmegabuf.</summary>
public sealed class EelGlobals
{
    public double[] Registers { get; } = new double[100];

    public EelMemory Megabuf { get; } = new();
}

/// <summary>
/// megabuf and gmegabuf: a big array of numbers, allocated in blocks only where it's used, so
/// presets that touch a few slots don't cost megabytes. Reads outside it give 0; writes are dropped.
/// </summary>
public sealed class EelMemory
{
    private const int BlockBits = 16;
    private const int BlockSize = 1 << BlockBits;
    private const int BlockCount = 128;

    /// <summary>How many slots there are (about 8 million, as in ns-eel2's default).</summary>
    public const int Capacity = BlockSize * BlockCount;

    private readonly double[]?[] _blocks = new double[BlockCount][];

    public double Get(double at)
    {
        var index = ToIndex(at);
        if (index < 0)
        {
            return 0;
        }

        var block = _blocks[index >> BlockBits];
        return block is null ? 0 : block[index & (BlockSize - 1)];
    }

    public double Set(double at, double value)
    {
        var index = ToIndex(at);
        if (index >= 0)
        {
            var block = _blocks[index >> BlockBits] ??= new double[BlockSize];
            block[index & (BlockSize - 1)] = value;
        }

        return value;
    }

    // Slots are numbered from a rounded-down number, with a little slack for 2.9999999 meaning 3.
    private static int ToIndex(double at)
    {
        var index = Math.Floor(at + 0.0001);
        return index >= 0 && index < Capacity ? (int)index : -1;
    }
}
