#include <catch2/catch_test_macros.hpp>

#include "file_open_log.h"

using fm4_static_recomp::FileOpenLogLine;

TEST_CASE("file open log lines are lower case") {
  CHECK(FileOpenLogLine("game:\\Media\\UI\\Fonts.zip") ==
        "File open: game:\\media\\ui\\fonts.zip");
  CHECK(FileOpenLogLine("") == "File open: ");
}
