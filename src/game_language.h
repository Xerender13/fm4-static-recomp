#pragma once

#include <cstdint>
#include <span>
#include <string>
#include <string_view>

namespace fm4_static_recomp {

// fm4_language: the language FM4 shows, one of the text sets on Disc 1. The game picks its text
// and voice-over from the console's language and country settings; the country alone selects
// the regional sets (UK English, Mexican Spanish) and the ones without a console language
// (Czech, Hungarian).
struct GameLanguage {
  std::string_view code;
  uint32_t language;  // user_language
  uint32_t country;   // user_country
};

std::span<const GameLanguage> GameLanguages();

// Case-insensitive; null for a code the game has no text for.
const GameLanguage* FindGameLanguage(std::string_view code);

// "en-US, en-GB, ...", for the message that refuses an unknown code.
std::string GameLanguageCodes();

// Sets user_language and user_country from fm4_language, when it is given. False, with the
// valid codes logged, for a code the game has no text for: the start stops there.
bool ApplyGameLanguage();

}  // namespace fm4_static_recomp
