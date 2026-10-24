#include "file_open_log.h"

#include <rex/cvar.h>
#include <rex/kernel/xboxkrnl/io.h>
#include <rex/logging.h>

REXCVAR_DEFINE_BOOL(fm4_log_file_opens, false, "Forza Motorsport 4",
                    "Log every guest file the game opens (the render tests wait on them)");

namespace fm4_static_recomp {

namespace {

void LogFileOpen(std::string_view guest_path) {
  if (!REXCVAR_GET(fm4_log_file_opens)) {
    return;
  }
  REXLOG_INFO("{}", FileOpenLogLine(guest_path));
}

}  // namespace

std::string FileOpenLogLine(std::string_view guest_path) {
  std::string line = "File open: ";
  for (const char character : guest_path) {
    line += character >= 'A' && character <= 'Z' ? static_cast<char>(character + ('a' - 'A'))
                                                 : character;
  }
  return line;
}

void InstallFileOpenLog() { rex::kernel::xboxkrnl::SetGuestFileOpenObserver(&LogFileOpen); }

}  // namespace fm4_static_recomp
