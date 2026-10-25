using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Tests.Settings;

public class GameLanguagesTests
{
    [Fact]
    public void Offers_the_codes_the_game_takes_in_its_order()
    {
        // src/game_language.cpp's list; the game refuses any other fm4_language.
        Assert.Equal(
            ["en-US", "en-GB", "fr", "de", "it", "es", "es-MX", "pl", "ru", "ja", "ko", "zh-TW", "pt-BR", "cs", "hu", "nl"],
            GameLanguages.All.Select(l => l.Code));
    }

    [Fact]
    public void Names_each_language_in_itself_once()
    {
        Assert.Equal(GameLanguages.All.Count, GameLanguages.All.Select(l => l.Name).Distinct().Count());
        Assert.Equal("Русский", GameLanguages.Find("ru")?.Name);
        Assert.Equal("English (US)", GameLanguages.Default.Name);
    }

    [Fact]
    public void Finds_nothing_for_a_code_it_does_not_offer()
    {
        Assert.Null(GameLanguages.Find("zh-CN"));
        Assert.Null(GameLanguages.Find(null));
    }
}
