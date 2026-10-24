#include "string_log.h"

#include <rex/cvar.h>

REXCVAR_DEFINE_BOOL(fm4_log_strings, false, "Forza Motorsport 4",
                    "Log the strings the game looks up (the render tests wait on screen titles)");

namespace fm4_static_recomp {

namespace {

constexpr double kShownAgainSeconds = 1.0;

void AppendUtf8(std::string& out, char32_t c) {
  if (c < 0x80) {
    out += char(c);
  } else if (c < 0x800) {
    out += char(0xC0 | (c >> 6));
    out += char(0x80 | (c & 0x3F));
  } else {
    out += char(0xE0 | (c >> 12));
    out += char(0x80 | ((c >> 6) & 0x3F));
    out += char(0x80 | (c & 0x3F));
  }
}

}  // namespace

std::string StringLogLine(std::u16string_view text) {
  std::string line = "String: ";
  for (const char16_t c : text) {
    AppendUtf8(line, c);
  }
  return line;
}

bool StringShownLog::Shown(std::u16string_view text, double seconds) {
  const auto [entry, inserted] = last_seen_.try_emplace(std::u16string(text), seconds);
  const double last = entry->second;
  entry->second = seconds;
  return inserted || seconds - last > kShownAgainSeconds;
}

bool StringLogEnabled() { return REXCVAR_GET(fm4_log_strings); }

}  // namespace fm4_static_recomp
