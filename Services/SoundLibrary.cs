using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace TraySounds.Services;

/// <summary>A playable sound: one of the stock sounds shipped in the package, or one the user added.</summary>
public sealed record Sound(string Id, string Name, string Emoji, bool IsStock, string FilePath);

/// <summary>How a user sound is stored in library.json. The audio itself lives in LocalFolder\Sounds.</summary>
internal sealed record UserSoundRecord(string Id, string Name, string FileName, bool IsRecording);

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

    private static readonly IReadOnlyList<Sound> s_stock =
    [
        Stock("airhorn", "Air horn", "📯"),
        Stock("rimshot", "Ba dum tss", "🥁"),
        Stock("sad-trombone", "Sad trombone", "🎺"),
        Stock("tada", "Ta-da", "🎉"),
        Stock("applause", "Applause", "👏"),
        Stock("crickets", "Crickets", "🦗"),
        Stock("correct", "Correct", "✅"),
        Stock("buzzer", "Wrong", "❌"),
        Stock("ding", "Ding", "🔔"),
        Stock("boing", "Boing", "🪀"),
        Stock("laser", "Pew", "⚡"),
        Stock("whoosh", "Whoosh", "💨"),
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
    public static async Task<Sound> AddAsync(string name, string sourcePath, bool isRecording)
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

        UserSoundRecord record = new(id, UniqueName(SoundNamePolicy.Clean(name) is { Length: > 0 } clean ? clean : "Sound"), fileName, isRecording);
        LoadUser().Insert(0, record);
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

    private static Sound Stock(string file, string name, string emoji) =>
        new("stock:" + file, name, emoji, true, Path.Combine(StockFolder, file + ".wav"));

    private static Sound ToSound(UserSoundRecord r) =>
        new(r.Id, r.Name, r.IsRecording ? RecordingEmoji : FileEmoji, false, Path.Combine(SoundsFolder, r.FileName));

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
