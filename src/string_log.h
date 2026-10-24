#pragma once

#include <string>
#include <string_view>
#include <unordered_map>

namespace fm4_static_recomp {

// fm4_log_strings: logs the strings the game looks up in its string tables, so the render
// tests' runner can tell which screen is up: a screen looks up its title once as it opens.
std::string StringLogLine(std::u16string_view text);

// The race HUD looks up its strings every frame: a string is logged when it was not looked up
// for a second.
class StringShownLog {
 public:
  bool Shown(std::u16string_view text, double seconds);

 private:
  std::unordered_map<std::u16string, double> last_seen_;
};

bool StringLogEnabled();

}  // namespace fm4_static_recomp
