#include "game_language.h"

#include <array>
#include <cctype>

#include <rex/cvar.h>
#include <rex/logging.h>

REXCVAR_DEFINE_STRING(fm4_language, "", "Forza Motorsport 4",
                      "The game's language: en-US, en-GB, fr, de, it, es, es-MX, pl, ru, ja, ko, "
                      "zh-TW, pt-BR, cs, hu or nl; empty keeps user_language and user_country");

namespace fm4_static_recomp {
namespace {

// Each pair checked against the StringTables and VO files the game opens with it. Czech and
// Hungarian come from the country under English; Simplified Chinese, Swedish, Turkish and
// Norwegian fall back to US English, so they are not offered.
constexpr std::array kLanguages = {
    GameLanguage{"en-US", 1, 103}, GameLanguage{"en-GB", 1, 35}, GameLanguage{"fr", 4, 34},
    GameLanguage{"de", 3, 24},     GameLanguage{"it", 6, 50},    GameLanguage{"es", 5, 31},
    GameLanguage{"es-MX", 5, 71},  GameLanguage{"pl", 11, 82},   GameLanguage{"ru", 12, 88},
    GameLanguage{"ja", 2, 53},     GameLanguage{"ko", 7, 56},    GameLanguage{"zh-TW", 8, 101},
    GameLanguage{"pt-BR", 9, 13},  GameLanguage{"cs", 1, 23},    GameLanguage{"hu", 1, 42},
    GameLanguage{"nl", 16, 74},
};

bool EqualsIgnoringCase(std::string_view a, std::string_view b) {
  if (a.size() != b.size()) {
    return false;
  }
  for (size_t i = 0; i < a.size(); ++i) {
    if (std::tolower(static_cast<unsigned char>(a[i])) !=
        std::tolower(static_cast<unsigned char>(b[i]))) {
      return false;
    }
  }
  return true;
}

}  // namespace

std::span<const GameLanguage> GameLanguages() {
  return kLanguages;
}

const GameLanguage* FindGameLanguage(std::string_view code) {
  for (const auto& language : kLanguages) {
    if (EqualsIgnoringCase(language.code, code)) {
      return &language;
    }
  }
  return nullptr;
}

bool ApplyGameLanguage() {
  const std::string code = REXCVAR_GET(fm4_language);
  if (code.empty()) {
    return true;
  }
  const GameLanguage* const language = FindGameLanguage(code);
  if (!language) {
    REXLOG_ERROR("Language: the game has no text for fm4_language={}; use one of {}", code,
                 GameLanguageCodes());
    return false;
  }
  rex::cvar::SetFlagByName("user_language", std::to_string(language->language));
  rex::cvar::SetFlagByName("user_country", std::to_string(language->country));
  REXLOG_INFO("Language: {} (user_language {}, user_country {})", language->code,
              language->language, language->country);
  return true;
}

std::string GameLanguageCodes() {
  std::string codes;
  for (const auto& language : kLanguages) {
    if (!codes.empty()) {
      codes += ", ";
    }
    codes += language.code;
  }
  return codes;
}

}  // namespace fm4_static_recomp
