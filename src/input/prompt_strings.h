#pragma once

#include <cstdint>
#include <optional>
#include <span>
#include <string>
#include <string_view>

#include "input/controls.h"

namespace fm4_static_recomp::input {

// The string keys (16-bit, the same in every table) of the English strings
// that name pad buttons in prose.
std::span<const uint16_t> PromptStringKeys();
bool IsPromptStringKey(uint16_t key);

// The game's text with each pad button phrase replaced by the keys bound to it
// (the driving keys for in-race messages, the menu keys otherwise); nullopt
// when the text names no button (other languages, or another table's string
// under the same key).
std::optional<std::u16string> KeyboardText(uint16_t key, std::u16string_view text,
                                           const Controls& controls);

}  // namespace fm4_static_recomp::input
