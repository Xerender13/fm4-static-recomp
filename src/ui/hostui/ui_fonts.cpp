#include "ui/hostui/ui_fonts.h"

#include <cstdint>
#include <fstream>
#include <utility>
#include <vector>

#include "ui/hostui/ui_archive.h"
#include "ui/hostui/vector_font.h"

namespace fm4_static_recomp::hostui {

bool ExportUiFonts(const std::filesystem::path& fonts_zip, const std::filesystem::path& out_dir,
                   std::string* error) {
  const auto archive = UiArchive::Open(fonts_zip);
  if (!archive) {
    *error = "cannot open " + fonts_zip.string();
    return false;
  }
  std::vector<std::vector<uint8_t>> fonts;
  for (const char* member : kUiFontMembers) {
    auto data = archive->Read(member);
    if (!data) {
      *error = std::string("no readable ") + member + " in " + fonts_zip.string();
      return false;
    }
    if (!VectorFont::Parse(*data)) {
      *error = std::string(member) + " is not a vector font";
      return false;
    }
    fonts.push_back(std::move(*data));
  }
  std::error_code ec;
  std::filesystem::create_directories(out_dir, ec);
  for (size_t i = 0; i < fonts.size(); ++i) {
    const auto path = out_dir / kUiFontMembers[i];
    std::ofstream file(path, std::ios::binary | std::ios::trunc);
    file.write(reinterpret_cast<const char*>(fonts[i].data()), std::streamsize(fonts[i].size()));
    if (!file) {
      *error = "cannot write " + path.string();
      return false;
    }
  }
  return true;
}

}  // namespace fm4_static_recomp::hostui
