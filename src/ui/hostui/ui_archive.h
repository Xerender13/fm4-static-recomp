#pragma once

#include <cstddef>
#include <cstdint>
#include <filesystem>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace fm4_static_recomp::hostui {

// Splits an Xbox XMem stream (ZIP method 21) into the raw LZX blocks that
// lzx_decompress expects. Blocks carry a two-byte compressed size, or 0xFF,
// the uncompressed size and the compressed size when shorter than 32 KiB.
bool ParseXmemPayload(std::span<const uint8_t> compressed, size_t uncompressed_size,
                      std::vector<uint8_t>& payload);

// Decompresses one XMem LZX member payload, trying the window sizes the title
// uses.
std::optional<std::vector<uint8_t>> DecompressXmem(std::span<const uint8_t> compressed,
                                                   size_t uncompressed_size);

uint32_t Crc32(std::span<const uint8_t> data);

// Read-only access to a UI archive: an ordinary ZIP whose members are
// stored or XMem LZX compressed. Member names match case-insensitively.
class UiArchive {
 public:
  static constexpr size_t kMaximumMemberSize = 64 * 1024 * 1024;

  static std::optional<UiArchive> Open(const std::filesystem::path& path);

  // The member's bytes after decompression and a CRC check, or nullopt.
  std::optional<std::vector<uint8_t>> Read(std::string_view name) const;

 private:
  struct Member {
    uint64_t header_offset = 0;
    uint32_t compressed_size = 0;
    uint32_t size = 0;
    uint32_t crc32 = 0;
    uint16_t method = 0;
  };

  std::filesystem::path path_;
  std::unordered_map<std::string, Member> members_;
};

}  // namespace fm4_static_recomp::hostui
