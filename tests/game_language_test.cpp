#include <catch2/catch_test_macros.hpp>

#include <set>
#include <string>

#include <rex/cvar.h>
#include <rex/system/flags.h>

#include "game_language.h"

using fm4_static_recomp::ApplyGameLanguage;
using fm4_static_recomp::FindGameLanguage;
using fm4_static_recomp::GameLanguageCodes;
using fm4_static_recomp::GameLanguages;

namespace {

void RequireLanguage(const char* code, uint32_t language, uint32_t country) {
  const auto* found = FindGameLanguage(code);
  INFO(code);
  REQUIRE(found != nullptr);
  CHECK(found->language == language);
  CHECK(found->country == country);
}

}  // namespace

TEST_CASE("Each game language sets the console language and country the game reads it from") {
  RequireLanguage("en-US", 1, 103);
  RequireLanguage("en-GB", 1, 35);
  RequireLanguage("fr", 4, 34);
  RequireLanguage("de", 3, 24);
  RequireLanguage("it", 6, 50);
  RequireLanguage("es", 5, 31);
  RequireLanguage("es-MX", 5, 71);
  RequireLanguage("pl", 11, 82);
  RequireLanguage("ru", 12, 88);
  RequireLanguage("ja", 2, 53);
  RequireLanguage("ko", 7, 56);
  RequireLanguage("zh-TW", 8, 101);
  RequireLanguage("pt-BR", 9, 13);
  RequireLanguage("cs", 1, 23);
  RequireLanguage("hu", 1, 42);
  RequireLanguage("nl", 16, 74);
  CHECK(GameLanguages().size() == 16);
}

TEST_CASE("Game language codes are matched without regard to case") {
  REQUIRE(FindGameLanguage("EN-gb") != nullptr);
  CHECK(FindGameLanguage("EN-gb")->country == 35);
}

TEST_CASE("A code the game has no text for is refused") {
  CHECK(FindGameLanguage("") == nullptr);
  CHECK(FindGameLanguage("zh-CN") == nullptr);
  CHECK(FindGameLanguage("sv") == nullptr);
  CHECK(FindGameLanguage("en") == nullptr);
}

TEST_CASE("The list of codes names every language once") {
  const std::string codes = GameLanguageCodes();
  CHECK(codes.rfind("en-US, en-GB, ", 0) == 0);
  std::set<std::string> unique;
  for (const auto& language : GameLanguages()) {
    CHECK(codes.find(std::string(language.code)) != std::string::npos);
    unique.emplace(language.code);
  }
  CHECK(unique.size() == GameLanguages().size());
}

TEST_CASE("fm4_language sets the console language and country before the game reads them") {
  REQUIRE(rex::cvar::SetFlagByName("user_language", "1"));
  REQUIRE(rex::cvar::SetFlagByName("user_country", "103"));
  REQUIRE(rex::cvar::SetFlagByName("fm4_language", "ru"));
  CHECK(ApplyGameLanguage());
  CHECK(REXCVAR_GET(user_language) == 12);
  CHECK(REXCVAR_GET(user_country) == 88);
  REQUIRE(rex::cvar::SetFlagByName("fm4_language", ""));
}

TEST_CASE("Without fm4_language the console settings given at launch stay") {
  REQUIRE(rex::cvar::SetFlagByName("user_language", "3"));
  REQUIRE(rex::cvar::SetFlagByName("user_country", "24"));
  REQUIRE(rex::cvar::SetFlagByName("fm4_language", ""));
  CHECK(ApplyGameLanguage());
  CHECK(REXCVAR_GET(user_language) == 3);
  CHECK(REXCVAR_GET(user_country) == 24);
}

TEST_CASE("An unknown fm4_language stops the start and changes nothing") {
  REQUIRE(rex::cvar::SetFlagByName("user_language", "1"));
  REQUIRE(rex::cvar::SetFlagByName("user_country", "103"));
  REQUIRE(rex::cvar::SetFlagByName("fm4_language", "klingon"));
  CHECK_FALSE(ApplyGameLanguage());
  CHECK(REXCVAR_GET(user_language) == 1);
  CHECK(REXCVAR_GET(user_country) == 103);
  REQUIRE(rex::cvar::SetFlagByName("fm4_language", ""));
}
