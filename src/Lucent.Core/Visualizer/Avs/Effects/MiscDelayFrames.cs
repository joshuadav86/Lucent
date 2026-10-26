namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// The frames a delay effect (Video Delay, Multi Delay) keeps, as AVS's one block of memory held them a
/// picture at a time: a row of slots that each hold a picture, or nothing (black, as fresh memory was).
/// Moving slots around shares pictures between them rather than copying; writing to a slot whose picture
/// another slot shares gives it its own first.
/// </summary>
internal sealed class MiscDelayFrames
{
    private int[]?[] _slots = Array.Empty<int[]?>();

    /// <summary>How many slots there are (AVS's buffer size, in frames).</summary>
    public int Count => _slots.Length;

    /// <summary>
    /// The longest delay, in frames, that fits in <paramref name="budgetBytes"/> at this picture size:
    /// AVS's windows were small, so a delay of hundreds of frames at Lucent's sizes would take gigabytes.
    /// </summary>
    public static int MaxFrames(int pixels, long budgetBytes) => (int)Math.Clamp(budgetBytes / Math.Max(1L, pixels * 4L), 1, int.MaxValue);

    /// <summary>Starts over with <paramref name="count"/> black slots.</summary>
    public void Reset(int count) => _slots = count <= 0 ? Array.Empty<int[]?>() : new int[]?[count];

    /// <summary>Copies the picture in <paramref name="slot"/> to <paramref name="target"/> (black for an empty or missing slot).</summary>
    public void CopyOut(int slot, int[] target, int length)
    {
        var picture = (uint)slot < (uint)_slots.Length ? _slots[slot] : null;
        if (picture is null || picture.Length != length)
        {
            Array.Clear(target, 0, length);
        }
        else
        {
            Array.Copy(picture, target, length);
        }
    }

    /// <summary>Copies <paramref name="source"/> into <paramref name="slot"/>.</summary>
    public void CopyIn(int slot, int[] source, int length)
    {
        if ((uint)slot >= (uint)_slots.Length)
        {
            return;
        }

        var picture = _slots[slot];
        if (picture is null || picture.Length != length || IsShared(slot, picture))
        {
            picture = new int[length];
            _slots[slot] = picture;
        }

        Array.Copy(source, picture, length);
    }

    /// <summary>MoveMemory: <paramref name="count"/> slots from <paramref name="from"/> to <paramref name="to"/>, overlapping or not.</summary>
    public void Move(int from, int to, int count)
    {
        if (from < 0 || to < 0)
        {
            return;
        }

        count = Math.Min(count, Math.Min(_slots.Length - from, _slots.Length - to));
        if (count > 0)
        {
            Array.Copy(_slots, from, _slots, to, count);
        }
    }

    /// <summary>Makes slots <paramref name="from"/> up to <paramref name="to"/> (not including it) the same picture as slot <paramref name="source"/>.</summary>
    public void Fill(int from, int to, int source)
    {
        if ((uint)source >= (uint)_slots.Length)
        {
            return;
        }

        for (var slot = Math.Max(0, from); slot < Math.Min(to, _slots.Length); slot++)
        {
            _slots[slot] = _slots[source];
        }
    }

    private bool IsShared(int slot, int[] picture)
    {
        for (var other = 0; other < _slots.Length; other++)
        {
            if (other != slot && ReferenceEquals(_slots[other], picture))
            {
                return true;
            }
        }

        return false;
    }
}
