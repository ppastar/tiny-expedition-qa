using TinyExpedition.Core.Data;

namespace TinyExpedition.QaTool;

public sealed class OptionException(string message) : Exception(message);

public sealed class Options
{
    private readonly Dictionary<string, string> _values = new();

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new OptionException("값 앞에 --옵션 이름이 필요합니다: " + args[i]);
            var key = args[i][2..];
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--")) throw new OptionException("--" + key + " 값이 없습니다.");
            o._values[key] = args[++i];
        }
        return o;
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public string Get(string key, string fallback) => Get(key) ?? fallback;
    public string Require(string key) => Get(key) ?? throw new OptionException("--" + key + " 옵션이 필요합니다.");

    public long GetLong(string key, long fallback)
    {
        var v = Get(key);
        if (v == null) return fallback;
        return long.TryParse(v.Replace("_", ""), out var n) ? n : throw new OptionException("--" + key + " 는 숫자여야 합니다: " + v);
    }
}

public static class Repo
{
    /// <summary>--data가 없으면 현재 폴더에서 위로 올라가며 UnityProject의 GameData 폴더를 찾는다.</summary>
    public static string DataDir(Options o)
    {
        var explicitDir = o.Get("data");
        if (explicitDir != null) return explicitDir;
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "UnityProject", "Assets", "StreamingAssets", "GameData");
            if (Directory.Exists(c)) return c;
            dir = dir.Parent;
        }
        throw new OptionException("GameData 폴더를 찾지 못했습니다. --data로 지정하세요.");
    }

    public static GameDatabase LoadDb(Options o)
    {
        var dir = DataDir(o);
        return GameDatabase.Load(f => File.ReadAllText(Path.Combine(dir, f)));
    }
}
