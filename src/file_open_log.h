#pragma once

#include <string>
#include <string_view>

namespace fm4_static_recomp {

// fm4_log_file_opens: logs every guest file the game opens, so the render tests' runner can tell
// which screen is up.
std::string FileOpenLogLine(std::string_view guest_path);

void InstallFileOpenLog();

}  // namespace fm4_static_recomp
