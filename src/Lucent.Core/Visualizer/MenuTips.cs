namespace Lucent.Visualizer;

/// <summary>
/// What the less obvious items in the visuals' right-click menu do, shown when the mouse rests on them. Shared so
/// Windows and Mac say the same thing.
/// </summary>
public static class MenuTips
{
    public const string Fullscreen = "Fill the screen with the visuals.";

    public const string LastShown = "Go back to the visual shown before this one, even one Auto picked.";

    public const string UpInList = "Go to the visual above this one in the picker.";

    public const string LucentOriginals = "Visuals made for Lucent. Off leaves them out of the picker and Auto.";

    public const string MilkDrop = "Classic visuals made by the MilkDrop community, played by Lucent.";

    public const string ShowMilkDrop = "List MilkDrop presets in the picker and play them in Auto.";

    public const string ModernMilkDrop = "Add Lucent's glow, richer color, and HDR highlights. Off shows presets as MilkDrop drew them.";

    public const string ImportMilkDrop = "Add .milk preset files you've downloaded.";

    public const string Avs = "Classic visuals made with AVS (Advanced Visualization Studio), played by Lucent.";

    public const string ShowAvs = "List AVS presets in the picker and play them in Auto.";

    public const string ModernAvs = "Draw AVS presets sharper and smoother, with Lucent's glow. Off shows them as they were first drawn.";

    public const string ImportAvs = "Add .avs preset files you've downloaded.";

    public const string ImportFolder = "Add every preset in a folder, such as a whole pack.";

    public const string PresetFolder = "Open the folder Lucent keeps these presets in.";

    public const string Auto = "Change to a new visual on the beat, every so often.";

    public const string ChangeEvery = "How long Auto stays on each visual. Picking a time turns Auto on.";

    public const string Resolution = "How many pixels visuals are drawn at. Automatic matches the window and draws fewer while a visual can't keep up, so it stays smooth. Higher is sharper but more work for the graphics card.";

    public const string Detail = "Finer detail in the 3D and fractal visuals. Higher is more work for the graphics card.";

    public const string Glow = "How much light blooms around the bright parts of the visuals.";

    public const string BuildUpsAndDrops = "Visuals close in and change color through a song's build-up, then burst on the drop.";

    public const string MouseMovesParticles = "Move the mouse over a particle visual to push its dots aside, or hold the button to grab and throw them.";

    public const string Hdr = "Let highlights shine brighter than normal white on an HDR screen.";

    public const string FrameRate = "Show how many frames a second the visuals are drawn at.";

    public const string FullFrameRateInBackground = "Keep the visuals smooth while you use another app, such as with Lucent on a second screen. They still stop drawing when minimized or hidden.";

    public const string FullScreenLucent = "Spread every open Lucent window across the screen, side by side.";

    public const string Tidy = "Line the windows up evenly. Choose it again for another arrangement.";

    public const string Layouts = "Save the way your windows are arranged, and switch back to it later.";

    public const string ResetLayout = "Put the windows back where Lucent first opens them.";
}
