using System.IO;
using Lucent.Editor.Dsp;
using NAudio.Wave;

namespace Lucent.Library;

/// <summary>
/// Checks that filling in missing BPM and key (<see cref="TempoKeyFiller"/>) hears made-up music right, writes only the
/// fields a file is missing, and leaves a file that has both alone. Both apps run it in their self-tests.
/// </summary>
public static class TempoKeySelfTest
{
    /// <summary>Runs the checks, reporting each through <paramref name="log"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lucent-tempo-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            return Check(folder, log);
        }
        catch (Exception ex)
        {
            log($"Tempo and key: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // Left in the temp folder.
            }
        }
    }

    private static int Check(string folder, Action<string> log)
    {
        var failures = 0;
        var path = Path.Combine(folder, "92 in D major.wav");
        var audio = MusicSelfTest.MakeMusic(92, 0.05, bars: 32, tonic: 2, minor: false);
        WaveFileWriter.CreateWaveFile16(path, new Editor.AudioBufferSampleProvider(audio));

        // Nothing tagged: both are filled in.
        var changes = TempoKeyFiller.Listen(path, CancellationToken.None);
        if (changes is null || Field(changes, TagField.Bpm) != "92" || Field(changes, TagField.Key) != "D")
        {
            log($"Tempo and key: an untagged song in D major at 92 BPM would get {Describe(changes)}.");
            return 1;
        }

        // A BPM of its own (even a different one) is kept: only the key is filled in.
        Save(path, new Dictionary<TagField, string> { [TagField.Bpm] = "184" });
        changes = TempoKeyFiller.Listen(path, CancellationToken.None);
        if (changes is null || changes.Fields.ContainsKey(TagField.Bpm) || Field(changes, TagField.Key) != "D")
        {
            log($"Tempo and key: a song tagged 184 BPM would get {Describe(changes)}, not just its key.");
            failures++;
        }

        Save(path, changes?.Fields ?? new Dictionary<TagField, string>());
        var tags = TrackTags.Read(path).Values;
        if (tags[TagField.Bpm] != "184" || tags[TagField.Key] != "D")
        {
            log($"Tempo and key: after filling in, the file says {tags[TagField.Bpm]} BPM in \"{tags[TagField.Key]}\".");
            failures++;
        }

        // Both there: nothing to do.
        if (TempoKeyFiller.Listen(path, CancellationToken.None) is { } again)
        {
            log($"Tempo and key: a song with both would still get {Describe(again)}.");
            failures++;
        }

        if (failures == 0)
        {
            log("Tempo and key: filled in 92 BPM and D, kept a BPM already there, and left a tagged song alone.");
        }

        return failures;
    }

    private static void Save(string path, IReadOnlyDictionary<TagField, string> fields)
    {
        var changes = new TagChanges();
        foreach (var (field, text) in fields)
        {
            changes.Fields[field] = text;
        }

        TagWriter.Commit(TagWriter.Prepare(path, changes));
    }

    private static string? Field(TagChanges changes, TagField field) => changes.Fields.TryGetValue(field, out var text) ? text : null;

    private static string Describe(TagChanges? changes) => changes is null
        ? "nothing"
        : string.Join(", ", changes.Fields.Select(pair => $"{TagFields.Label(pair.Key)} {pair.Value}"));
}
