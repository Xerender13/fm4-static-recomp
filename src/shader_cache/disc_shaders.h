#pragma once

#include <cstdint>
#include <filesystem>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <vector>

namespace fm4_static_recomp::shader_cache {

// One Xbox 360 shader's microcode as the disc holds it: big-endian guest
// bytes, hashed with XXH3-64 as the SDK's shader storage does.
struct DiscShader {
  uint64_t hash = 0;
  bool pixel = false;
  std::vector<uint8_t> ucode;
  std::string source;
};

// Every shader container (0x102A1100 / 0x102A1101) in data.
std::vector<DiscShader> FindContainers(std::span<const uint8_t> data, std::string_view source);

// The shaders of Disc 1, distinct by hash: the executable image, every file
// under Media\Shaders and the shaders\ members of each track's bin.zip.
// files_out receives the number of files read. nullopt when a folder cannot
// be listed; error then names it.
std::optional<std::vector<DiscShader>> ScanDisc(const std::filesystem::path& game_root,
                                                std::span<const uint8_t> image, std::string* error,
                                                size_t* files_out = nullptr);

// index.tsv (HASH16, vs|ps, dword count, source) and <HASH16>.bin per shader.
bool WriteDump(const std::vector<DiscShader>& shaders, const std::filesystem::path& dir);

}  // namespace fm4_static_recomp::shader_cache
