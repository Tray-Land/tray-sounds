using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace TraySounds.Services;

/// <summary>A playable sound: one of the stock sounds shipped in the package, or one the user added.</summary>
public sealed record Sound(string Id, string Name, string Emoji, bool IsStock, string FilePath, SoundCredit? Credit = null, SoundTrim? Trim = null);

/// <summary>How a user sound is stored in library.json. The audio itself lives in LocalFolder\Sounds.</summary>
internal sealed record UserSoundRecord(string Id, string Name, string FileName, bool IsRecording, string? Emoji = null, long StartMs = 0, long EndMs = 0);

[JsonSerializable(typeof(List<UserSoundRecord>))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class SoundLibraryJsonContext : JsonSerializerContext;

/// <summary>
/// The stock sounds plus the user's own, which are copied into the app's LocalFolder so they keep
/// working after the original file moves. The list is tiny, so it loads on first use and stays.
/// </summary>
internal static class SoundLibrary
{
    public const string RecordingEmoji = "🎙️";
    public const string FileEmoji = "🎵";

    private static readonly string StockFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");

    // Must stay above s_stock: static initializers run in textual order.
    private static readonly IReadOnlyDictionary<string, SoundCredit> s_credits = LoadCredits();

    // The id is kept from the old synthesized sounds where one was replaced, so saved tile order survives.
    private static readonly IReadOnlyList<Sound> s_stock =
    [
        Stock("freesound_community-dj-airhorn-sound-39405.mp3", "Air horn", "📯", "airhorn"),
        Stock("freesound_community-rimshot-joke-funny-80325.mp3", "Ba dum tss", "🥁", "rimshot"),
        Stock("universfield-failure-trumpet-242645.mp3", "Sad trumpet", "🎺", "sad-trombone"),
        Stock("376318__jimhancock__tada.mp3", "Ta-da", "🎉", "tada"),
        Stock("423455__ohforheavensake__trumpet-brass-fanfare.mp3", "Fanfare", "🎖️"),
        Stock("350433__bevibeldesign__horn-stabs-entrance.mp3", "Grand entrance", "🎷"),
        Stock("324323__kwahmah_02__huge-applause.mp3", "Applause", "👏", "applause"),
        Stock("124996__phmiller42__aww.mp3", "Aww", "🥺"),
        Stock("656116__itsthegoodstuff__crickets.mp3", "Crickets", "🦗", "crickets"),
        Stock("correct.wav", "Correct", "✅"),
        Stock("buzzer.wav", "Wrong", "❌"),
        Stock("ding.wav", "Ding", "🔔"),
        Stock("188869__plingativator__jawharp_boing.mp3", "Boing", "🪀", "boing"),
        Stock("laser.wav", "Pew", "⚡"),
        Stock("425706__moogy73__woosh_medium_short_01.mp3", "Whoosh", "💨", "whoosh"),
    ];

    private static List<UserSoundRecord>? s_user;

    /// <summary>Raised on the calling thread after a user sound is added, renamed, or removed.</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<Sound> Stock() => s_stock;

    public static IReadOnlyList<Sound> UserSounds() => LoadUser().Select(ToSound).ToList();

    public static string SoundsFolder => Path.Combine(ApplicationData.Current.LocalFolder.Path, "Sounds");

    private static string LibraryPath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "library.json");

    public static Sound? Find(string id) =>
        s_stock.FirstOrDefault(s => s.Id == id) ?? UserSounds().FirstOrDefault(s => s.Id == id);

    /// <summary>A name suggestion that doesn't collide with an existing sound.</summary>
    public static string UniqueName(string name, string? exceptId = null) =>
        SoundNamePolicy.MakeUnique(name, s_stock.Select(s => s.Name).Concat(LoadUser().Where(u => u.Id != exceptId).Select(u => u.Name)));

    /// <summary>
    /// Copies (or, for a temporary recording, moves) <paramref name="sourcePath"/> into the library.
    /// </summary>
    public static async Task<Sound> AddAsync(string name, string sourcePath, bool isRecording, string? emoji = null, SoundTrim? trim = null)
    {
        string id = Guid.NewGuid().ToString("N");
        string fileName = SoundNamePolicy.StorageFileName(id, sourcePath);
        string target = Path.Combine(SoundsFolder, fileName);
        Directory.CreateDirectory(SoundsFolder);

        await Task.Run(() =>
        {
            if (isRecording)
            {
                File.Move(sourcePath, target);
            }
            else
            {
                File.Copy(sourcePath, target);
            }
        });

        UserSoundRecord record = new(id, UniqueName(SoundNamePolicy.Clean(name) is { Length: > 0 } clean ? clean : "Sound"), fileName, isRecording, SoundNamePolicy.FirstEmoji(emoji, DefaultEmoji(isRecording)), TrimStartMs(trim), TrimEndMs(trim));
        LoadUser().Add(record);
        Save();
        return ToSound(record);
    }

    public static void Rename(string id, string name)
    {
        List<UserSoundRecord> user = LoadUser();
        int index = user.FindIndex(u => u.Id == id);
        string clean = SoundNamePolicy.Clean(name);
        if (index < 0 || clean.Length == 0)
        {
            return;
        }

        user[index] = user[index] with { Name = UniqueName(clean, id) };
        Save();
    }

    public static void SetEmoji(string id, string emoji)
    {
        List<UserSoundRecord> user = LoadUser();
        int index = user.FindIndex(u => u.Id == id);
        if (index < 0)
        {
            return;
        }

        user[index] = user[index] with { Emoji = SoundNamePolicy.FirstEmoji(emoji, DefaultEmoji(user[index].IsRecording)) };
        Save();
    }

    /// <summary>Saves the edit window's result for a user sound: name, emoji, and the trimmed slice.</summary>
    public static void Update(string id, string name, string emoji, SoundTrim? trim)
    {
        List<UserSoundRecord> user = LoadUser();
        int index = user.FindIndex(u => u.Id == id);
        string clean = SoundNamePolicy.Clean(name);
        if (index < 0 || clean.Length == 0)
        {
            return;
        }

        user[index] = user[index] with
        {
            Name = UniqueName(clean, id),
            Emoji = SoundNamePolicy.FirstEmoji(emoji, DefaultEmoji(user[index].IsRecording)),
            StartMs = TrimStartMs(trim),
            EndMs = TrimEndMs(trim),
        };
        Save();
    }

    private static long TrimStartMs(SoundTrim? trim) => trim is { } t ? (long)t.Start.TotalMilliseconds : 0;

    private static long TrimEndMs(SoundTrim? trim) => trim is { } t ? (long)t.End.TotalMilliseconds : 0;

    public static string DefaultEmoji(bool isRecording) => isRecording ? RecordingEmoji : FileEmoji;

    public static void Remove(string id)
    {
        List<UserSoundRecord> user = LoadUser();
        UserSoundRecord? record = user.Find(u => u.Id == id);
        if (record is null)
        {
            return;
        }

        user.Remove(record);
        Save();
        try
        {
            File.Delete(Path.Combine(SoundsFolder, record.FileName));
        }
        catch
        {
            // Still playing or locked; the orphan is harmless and tiny.
        }
    }

    private static Sound Stock(string file, string name, string emoji, string? id = null)
    {
        string stem = Path.GetFileNameWithoutExtension(file);
        return new("stock:" + (id ?? stem), name, emoji, true, Path.Combine(StockFolder, file), SoundCreditPolicy.Find(s_credits, stem));
    }

    private static IReadOnlyDictionary<string, SoundCredit> LoadCredits()
    {
        try
        {
            return SoundCreditPolicy.Parse(File.ReadAllText(Path.Combine(StockFolder, "sound attribution.csv")));
        }
        catch
        {
            // Credits are a courtesy; the sounds still play without them.
            return new Dictionary<string, SoundCredit>();
        }
    }

    private static Sound ToSound(UserSoundRecord r) =>
        new(r.Id, r.Name, r.Emoji is { Length: > 0 } emoji ? emoji : DefaultEmoji(r.IsRecording), false, Path.Combine(SoundsFolder, r.FileName), null, SoundTrimPolicy.FromStored(r.StartMs, r.EndMs));

    private static List<UserSoundRecord> LoadUser()
    {
        if (s_user is not null)
        {
            return s_user;
        }

        try
        {
            s_user = File.Exists(LibraryPath)
                ? JsonSerializer.Deserialize(File.ReadAllText(LibraryPath), SoundLibraryJsonContext.Default.ListUserSoundRecord) ?? []
                : [];
        }
        catch
        {
            // A corrupt library shouldn't take the stock sounds down with it.
            s_user = [];
        }

        // Drop entries whose audio went missing (e.g. LocalState was cleaned by hand).
        s_user.RemoveAll(u => !File.Exists(Path.Combine(SoundsFolder, u.FileName)));
        return s_user;
    }

    private static void Save()
    {
        try
        {
            string temp = LibraryPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(LoadUser(), SoundLibraryJsonContext.Default.ListUserSoundRecord));
            File.Move(temp, LibraryPath, overwrite: true);
        }
        catch
        {
            // Best effort; the in-memory list still reflects the change for this session.
        }

        Changed?.Invoke(null, EventArgs.Empty);
    }
}
