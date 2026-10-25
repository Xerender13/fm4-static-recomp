namespace Fm4StaticRecomp.Launcher.Core.Settings;

public sealed record GameLanguage(string Code, string Name);

// The text sets on Disc 1 the game's fm4_language takes (src/game_language.cpp), each named in
// itself. Korean, Chinese, Portuguese, Czech, Hungarian and Dutch play the English voice-over.
public static class GameLanguages
{
    public static IReadOnlyList<GameLanguage> All { get; } =
    [
        new("en-US", "English (US)"), new("en-GB", "English (UK)"), new("fr", "Français"), new("de", "Deutsch"),
        new("it", "Italiano"), new("es", "Español (España)"), new("es-MX", "Español (México)"), new("pl", "Polski"),
        new("ru", "Русский"), new("ja", "日本語"), new("ko", "한국어"), new("zh-TW", "繁體中文"),
        new("pt-BR", "Português (Brasil)"), new("cs", "Čeština"), new("hu", "Magyar"), new("nl", "Nederlands"),
    ];

    public static GameLanguage Default => All[0];

    public static GameLanguage? Find(string? code) => All.FirstOrDefault(l => l.Code == code);
}
