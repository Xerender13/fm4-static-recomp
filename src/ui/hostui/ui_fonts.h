#pragma once

#include <filesystem>
#include <string>

namespace fm4_static_recomp::hostui {

// Fonts.zip members (fontmap.xml: A_Swiss_721_Bold_Condensed -> A,
// B_Swiss_721_Medium -> B).
inline constexpr const char* kUiFontMembers[] = {"a_vector_aa.dt", "b_vector_aa.dt"};

// Writes each font member of the disc's Fonts.zip, decompressed, as
// `<out_dir>\<member>` for the launcher, which cannot decode XMem LZX itself.
// Every member must parse as a vector font; false with the reason otherwise.
bool ExportUiFonts(const std::filesystem::path& fonts_zip, const std::filesystem::path& out_dir,
                   std::string* error);

}  // namespace fm4_static_recomp::hostui
