#include "shader_cache/disc_shaders.h"

#include <algorithm>
#include <cstdio>
#include <fstream>
#include <iterator>
#include <optional>
#include <unordered_set>

#include <fmt/format.h>
#include <rex/filesystem.h>
#include <rex/hash.h>

#include "ui/hostui/ui_archive.h"

namespace fm4_static_recomp::shader_cache {
namespace {

constexpr size_t kContainerHeaderSize = 0x24;

uint32_t ReadBig32(const uint8_t* data) {
  return uint32_t(data[0]) << 24 | uint32_t(data[1]) << 16 | uint32_t(data[2]) << 8 |
         uint32_t(data[3]);
}

// nullopt when the folder cannot be listed completely.
std::optional<std::vector<std::filesystem::path>> SortedFiles(const std::filesystem::path& root,
                                                              bool recursive) {
  std::vector<std::filesystem::path> files;
  std::error_code error;
  if (recursive) {
    for (std::filesystem::recursive_directory_iterator it(root, error), end; !error && it != end;
         it.increment(error)) {
      if (it->is_regular_file(error)) {
        files.push_back(it->path());
      }
    }
  } else {
    for (std::filesystem::directory_iterator it(root, error), end; !error && it != end;
         it.increment(error)) {
      files.push_back(it->path());
    }
  }
  if (error) {
    return std::nullopt;
  }
  std::sort(files.begin(), files.end());
  return files;
}

std::optional<std::vector<uint8_t>> ReadFile(const std::filesystem::path& path) {
  std::ifstream stream(rex::filesystem::ExtendedLengthPath(path), std::ios::binary);
  if (!stream) {
    return std::nullopt;
  }
  return std::vector<uint8_t>(std::istreambuf_iterator<char>(stream), {});
}

}  // namespace

std::vector<DiscShader> FindContainers(std::span<const uint8_t> data, std::string_view source) {
  std::vector<DiscShader> shaders;
  for (size_t i = 0; i + kContainerHeaderSize <= data.size(); ++i) {
    if (data[i] != 0x10 || data[i + 1] != 0x2A || data[i + 2] != 0x11 || data[i + 3] > 1) {
      continue;
    }
    const uint8_t* header = &data[i];
    const uint32_t flags = ReadBig32(header);
    const uint64_t vertex_part = ReadBig32(header + 4);
    const uint64_t pixel_part = ReadBig32(header + 8);
    const uint64_t shader_offset = ReadBig32(header + 24);
    if (ReadBig32(header + 28) || ReadBig32(header + 32) ||
        vertex_part + pixel_part > data.size() - i || shader_offset + 8 > vertex_part) {
      continue;
    }
    const uint64_t ucode_offset = ReadBig32(header + shader_offset);
    const uint64_t ucode_size = ReadBig32(header + shader_offset + 4);
    const uint64_t base = i + vertex_part + ucode_offset;
    if (base + ucode_size > i + vertex_part + pixel_part || ucode_size % 4) {
      continue;
    }
    DiscShader shader;
    shader.pixel = !(flags & 1);
    shader.ucode.assign(data.begin() + base, data.begin() + base + ucode_size);
    shader.hash = XXH3_64bits(shader.ucode.data(), shader.ucode.size());
    shader.source = source;
    shaders.push_back(std::move(shader));
  }
  return shaders;
}

std::optional<std::vector<DiscShader>> ScanDisc(const std::filesystem::path& game_root,
                                                std::span<const uint8_t> image, std::string* error,
                                                size_t* files_out) {
  const std::filesystem::path shader_dir = game_root / "Media" / "Shaders";
  const std::filesystem::path track_dir = game_root / "Media" / "tracks";
  const auto shader_files = SortedFiles(shader_dir, true);
  const auto tracks = SortedFiles(track_dir, false);
  if (!shader_files || !tracks) {
    if (error) {
      *error = "cannot list " + rex::path_to_utf8(shader_files ? track_dir : shader_dir);
    }
    return std::nullopt;
  }
  std::vector<DiscShader> shaders;
  std::unordered_set<uint64_t> seen;
  size_t files = 0;
  auto add = [&](std::span<const uint8_t> data, std::string_view source) {
    ++files;
    for (DiscShader& shader : FindContainers(data, source)) {
      if (seen.insert(shader.hash).second) {
        shaders.push_back(std::move(shader));
      }
    }
  };

  add(image, "image");
  for (const std::filesystem::path& path : *shader_files) {
    if (auto data = ReadFile(path)) {
      add(*data, path.lexically_relative(game_root).generic_string());
    }
  }
  for (const std::filesystem::path& track : *tracks) {
    const std::filesystem::path zip = track / "bin.zip";
    auto archive = hostui::UiArchive::Open(rex::filesystem::ExtendedLengthPath(zip));
    if (!archive) {
      continue;
    }
    const std::string zip_name = zip.lexically_relative(game_root).generic_string();
    for (const std::string& name : archive->MemberNames()) {
      if (!name.starts_with("shaders\\") && !name.starts_with("shaders/")) {
        continue;
      }
      if (auto data = archive->Read(name)) {
        add(*data, zip_name + ":" + name);
      }
    }
  }
  if (files_out) {
    *files_out = files;
  }
  return shaders;
}

bool WriteDump(const std::vector<DiscShader>& shaders, const std::filesystem::path& dir) {
  std::error_code error;
  std::filesystem::create_directories(rex::filesystem::ExtendedLengthPath(dir), error);
  std::vector<const DiscShader*> sorted;
  for (const DiscShader& shader : shaders) {
    sorted.push_back(&shader);
  }
  std::sort(sorted.begin(), sorted.end(),
            [](const DiscShader* a, const DiscShader* b) { return a->hash < b->hash; });
  std::string index;
  for (const DiscShader* shader : sorted) {
    const std::string name = fmt::format("{:016X}", shader->hash);
    FILE* file = rex::filesystem::OpenFile(rex::filesystem::ExtendedLengthPath(dir / (name + ".bin")), "wb");
    if (!file) {
      return false;
    }
    const bool written =
        fwrite(shader->ucode.data(), 1, shader->ucode.size(), file) == shader->ucode.size();
    if (fclose(file) || !written) {
      return false;
    }
    index += fmt::format("{}\t{}\t{}\t{}\n", name, shader->pixel ? "ps" : "vs",
                         shader->ucode.size() / 4, shader->source);
  }
  FILE* file = rex::filesystem::OpenFile(rex::filesystem::ExtendedLengthPath(dir / "index.tsv"), "wb");
  if (!file) {
    return false;
  }
  const bool written = fwrite(index.data(), 1, index.size(), file) == index.size();
  return !fclose(file) && written;
}

}  // namespace fm4_static_recomp::shader_cache
